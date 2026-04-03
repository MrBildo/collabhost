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
}
