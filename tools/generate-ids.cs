#:package Ulid@1.4.1

var count = args.Length > 0 ? int.Parse(args[0]) : 1;

for (var i = 0; i < count; i++)
{
    Console.WriteLine($"GUID: {Guid.NewGuid()}");
    Console.WriteLine($"ULID: {Ulid.NewUlid()}");

    if (i < count - 1)
    {
        Console.WriteLine();
    }
}
