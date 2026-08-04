using OpenRAG.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddOpenRagWorkerApplication(builder.Configuration);

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
