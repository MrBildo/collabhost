// Stage demo (card #440): a tiny ASP.NET Core minimal API.
//
// Collabhost injects the listen port via ASPNETCORE_URLS (the dotnet-app type's
// port-injection binding). Exposes "/" (served through the proxy route) and
// "/health" (the health-check binding).
var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.MapGet("/", () => "Auth Service — running on Collabhost stage.");
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.Run();
