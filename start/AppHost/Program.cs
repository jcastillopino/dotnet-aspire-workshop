var builder = DistributedApplication.CreateBuilder(args);

var api = builder.AddProject<Projects.Api>("api");

var web = builder.AddProject<Projects.MyWeatherHub>("myweatherhub")
    .WithReference(api)
    .WithExternalHttpEndpoints();

builder.Build().Run();
