using Collabhost.Api.Shared;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Shared;

public class RingBufferTests
{
    [Fact]
    public void Add_SingleItem_CountIsOne()
    {
        var buffer = new RingBuffer<string>(10);

        buffer.Add("hello");

        buffer.Count.ShouldBe(1);
    }

    [Fact]
    public void GetAll_ReturnsItemsInInsertionOrder()
    {
        var buffer = new RingBuffer<int>(10);

        buffer.Add(1);
        buffer.Add(2);
        buffer.Add(3);

        var result = buffer.GetAll();

        result.ShouldBe([1, 2, 3]);
    }

    [Fact]
    public void Add_ExceedsCapacity_OverwritesOldestItems()
    {
        var buffer = new RingBuffer<int>(3);

        buffer.Add(1);
        buffer.Add(2);
        buffer.Add(3);
        buffer.Add(4);

        buffer.Count.ShouldBe(3);

        var result = buffer.GetAll();

        result.ShouldBe([2, 3, 4]);
    }

    [Fact]
    public void GetLast_ReturnsRequestedCount()
    {
        var buffer = new RingBuffer<int>(10);

        buffer.Add(1);
        buffer.Add(2);
        buffer.Add(3);
        buffer.Add(4);
        buffer.Add(5);

        var result = buffer.GetLast(3);

        result.ShouldBe([3, 4, 5]);
    }

    [Fact]
    public void GetLast_RequestMoreThanCount_ReturnsAll()
    {
        var buffer = new RingBuffer<int>(10);

        buffer.Add(1);
        buffer.Add(2);

        var result = buffer.GetLast(5);

        result.ShouldBe([1, 2]);
    }

    [Fact]
    public void GetLast_AfterWrap_ReturnsCorrectItems()
    {
        var buffer = new RingBuffer<int>(3);

        buffer.Add(1);
        buffer.Add(2);
        buffer.Add(3);
        buffer.Add(4);
        buffer.Add(5);

        var result = buffer.GetLast(2);

        result.ShouldBe([4, 5]);
    }

    [Fact]
    public void GetAll_EmptyBuffer_ReturnsEmptyList()
    {
        var buffer = new RingBuffer<string>(10);

        var result = buffer.GetAll();

        result.ShouldBeEmpty();
    }

    [Fact]
    public void GetLast_EmptyBuffer_ReturnsEmptyList()
    {
        var buffer = new RingBuffer<string>(10);

        var result = buffer.GetLast(5);

        result.ShouldBeEmpty();
    }

    [Fact]
    public void Clear_ResetsCountToZero()
    {
        var buffer = new RingBuffer<int>(10);

        buffer.Add(1);
        buffer.Add(2);
        buffer.Add(3);

        buffer.Clear();

        buffer.Count.ShouldBe(0);
        buffer.GetAll().ShouldBeEmpty();
    }

    [Fact]
    public void Capacity_ReturnsConstructorValue()
    {
        var buffer = new RingBuffer<int>(42);

        buffer.Capacity.ShouldBe(42);
    }

    [Fact]
    public void DefaultCapacity_IsOneThousand()
    {
        var buffer = new RingBuffer<int>();

        buffer.Capacity.ShouldBe(1000);
    }

    // --- Subscription ---

    [Fact]
    public async Task Subscribe_ReceivesNewItems()
    {
        var buffer = new RingBuffer<string>(10);
        var reader = buffer.Subscribe();

        buffer.Add("hello");
        buffer.Add("world");

        var item1 = await reader.ReadAsync();
        var item2 = await reader.ReadAsync();

        item1.Id.ShouldBe(1);
        item1.Item.ShouldBe("hello");
        item2.Id.ShouldBe(2);
        item2.Item.ShouldBe("world");

        buffer.Unsubscribe(reader);
    }

    [Fact]
    public async Task Subscribe_MultipleSubscribers_AllReceive()
    {
        var buffer = new RingBuffer<int>(10);
        var reader1 = buffer.Subscribe();
        var reader2 = buffer.Subscribe();

        buffer.Add(42);

        var item1 = await reader1.ReadAsync();
        var item2 = await reader2.ReadAsync();

        item1.Id.ShouldBe(1);
        item1.Item.ShouldBe(42);
        item2.Id.ShouldBe(1);
        item2.Item.ShouldBe(42);

        buffer.Unsubscribe(reader1);
        buffer.Unsubscribe(reader2);
    }

    [Fact]
    public async Task Subscribe_OrderPreserved()
    {
        var buffer = new RingBuffer<int>(10);
        var reader = buffer.Subscribe();

        for (var i = 1; i <= 5; i++)
        {
            buffer.Add(i);
        }

        var items = new List<(long Id, int Item)>();

        for (var i = 0; i < 5; i++)
        {
            items.Add(await reader.ReadAsync());
        }

        items.Select(x => x.Item).ShouldBe([1, 2, 3, 4, 5]);
        items.Select(x => x.Id).ShouldBe([1, 2, 3, 4, 5]);

        buffer.Unsubscribe(reader);
    }

    [Fact]
    public async Task Subscribe_ChannelOverflow_DropsOldest()
    {
        var buffer = new RingBuffer<int>(1000);
        var reader = buffer.Subscribe();

        // Add more items than the channel capacity (256) without reading
        for (var i = 1; i <= 300; i++)
        {
            buffer.Add(i);
        }

        var items = new List<(long Id, int Item)>();

        while (reader.TryRead(out var item))
        {
            items.Add(item);
        }

        // Channel capacity is 256, so the oldest items should have been dropped
        items.Count.ShouldBe(256);
        items[^1].Item.ShouldBe(300);
        items[^1].Id.ShouldBe(300);

        buffer.Unsubscribe(reader);
    }

    [Fact]
    public async Task Unsubscribe_StopsDelivery()
    {
        var buffer = new RingBuffer<string>(10);
        var reader = buffer.Subscribe();

        buffer.Unsubscribe(reader);

        buffer.Add("after-unsubscribe");

        // The channel reader should be completed
        reader.Completion.IsCompleted.ShouldBeTrue();
    }

    // --- GetLastWithIds ---

    [Fact]
    public void GetLastWithIds_ReturnsCorrectIds()
    {
        var buffer = new RingBuffer<string>(10);

        buffer.Add("a");
        buffer.Add("b");
        buffer.Add("c");
        buffer.Add("d");
        buffer.Add("e");

        var result = buffer.GetLastWithIds(3);

        result.Count.ShouldBe(3);
        result[0].ShouldBe((3, "c"));
        result[1].ShouldBe((4, "d"));
        result[2].ShouldBe((5, "e"));
    }

    [Fact]
    public void GetLastWithIds_AfterWrap_CorrectIds()
    {
        var buffer = new RingBuffer<int>(3);

        // Fill past capacity: 1,2,3,4,5 -- buffer holds [3,4,5]
        for (var i = 1; i <= 5; i++)
        {
            buffer.Add(i);
        }

        var result = buffer.GetLastWithIds(3);

        result.Count.ShouldBe(3);
        result[0].ShouldBe((3, 3));
        result[1].ShouldBe((4, 4));
        result[2].ShouldBe((5, 5));
    }

    [Fact]
    public void GetLastWithIds_EmptyBuffer_ReturnsEmpty()
    {
        var buffer = new RingBuffer<int>(10);

        var result = buffer.GetLastWithIds(5);

        result.ShouldBeEmpty();
    }

    // --- Sequence ID ---

    [Fact]
    public async Task SequenceId_MonotonicallyIncreasing()
    {
        var buffer = new RingBuffer<int>(100);
        var reader = buffer.Subscribe();

        for (var i = 0; i < 10; i++)
        {
            buffer.Add(i);
        }

        var ids = new List<long>();

        for (var i = 0; i < 10; i++)
        {
            var item = await reader.ReadAsync();
            ids.Add(item.Id);
        }

        for (var i = 1; i < ids.Count; i++)
        {
            ids[i].ShouldBe(ids[i - 1] + 1);
        }

        buffer.Unsubscribe(reader);
    }

    [Fact]
    public async Task SequenceId_StartsAtOne()
    {
        var buffer = new RingBuffer<string>(10);
        var reader = buffer.Subscribe();

        buffer.Add("first");

        var item = await reader.ReadAsync();

        item.Id.ShouldBe(1);

        buffer.Unsubscribe(reader);
    }
}
