// Stage demo: a tiny ASP.NET Core minimal API (card #443).
//
// Collabhost injects the listen port via ASPNETCORE_URLS (the dotnet-app type's
// port-injection binding), so this needs no port wiring of its own. It exposes
// "/" (a page the proxy route serves) and "/health" (the health-check binding).
var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.MapGet("/", () => "Demo .NET API — running on Collabhost stage.");
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.Run();
