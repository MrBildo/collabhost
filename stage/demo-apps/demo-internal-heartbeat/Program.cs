// Stage demo: a long-running, non-HTTP supervised process (card #443).
//
// Stands in for the kind of upstream the internal-service type targets (a cache,
// a broker, a custom-protocol daemon) -- Collabhost supervises it but emits no
// proxy route. The periodic stdout line exercises the log ring buffer + SSE
// stream on the app detail page.
Console.WriteLine("demo-internal-heartbeat: starting (Collabhost stage internal-service demo)");

var beat = 0;

while (true)
{
    beat++;
    Console.WriteLine($"heartbeat {beat} — {DateTimeOffset.UtcNow:O}");
    Thread.Sleep(TimeSpan.FromSeconds(5));
}
