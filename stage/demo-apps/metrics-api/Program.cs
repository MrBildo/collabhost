// Stage demo (card #440): a tiny ASP.NET Core minimal API.
//
// Collabhost injects the listen port via ASPNETCORE_URLS (the dotnet-app type's
// port-injection binding), so this needs no port wiring of its own. It exposes
// "/" (a page the proxy route serves), "/health" (the health-check binding), and
// "/metrics" (a stand-in operational endpoint).
//
// The .csproj carries a few well-known NuGet references (Serilog, Dapper,
// FluentValidation, Newtonsoft.Json) so the app-detail Technology tab renders a
// real dependency probe. They are referenced for the probe, not exercised here.
using FluentValidation;

var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.MapGet("/", () => "Metrics API — running on Collabhost stage.");
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
app.MapGet("/metrics", () => Results.Ok(new { requests = 1842, errors = 0, uptimeSeconds = 86_400 }));

// Touch a referenced package so the reference is unambiguously real, not just a
// restore entry: a trivial validator that is never invoked at request time.
var validator = new InlineValidator<string>();
validator.RuleFor(value => value).NotEmpty();

app.Run();
