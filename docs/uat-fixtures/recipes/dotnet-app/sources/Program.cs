// UAT dotnet-app fixture - minimal ASP.NET Core API.
//
// Per the runbook §2.1: serves HTTP 200 on `/` and `/health`. Listens on
// $ASPNETCORE_URLS (URL form per the runbook §3.1 port-injection table).
// No DB seeding, no first-run banners.
//
// EF Core is referenced (csproj) but not used at runtime - it's purely there
// to give DotnetDependenciesData a notable dependency to surface in the probe
// panel.

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () => Results.Text("UAT dotnet-app fixture", "text/plain"));
app.MapGet("/health", () => Results.Text("ok", "text/plain"));

app.Run();
