var builder = DistributedApplication.CreateBuilder(args);

var api = builder.AddProject<Projects.Collabhost_Api>("api")
    .WithEndpoint("http", endpoint => endpoint.Port = 58400)
    .WithHttpHealthCheck("/health");

builder.AddViteApp("frontend", "../../frontend")
    .WithExternalHttpEndpoints()
    .WithReference(api)
    .WaitFor(api);

await builder.Build().RunAsync();
