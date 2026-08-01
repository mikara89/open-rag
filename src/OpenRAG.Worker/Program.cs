using Mediator;
using OpenRAG.Application;
using OpenRAG.Infrastructure;
using OpenRAG.Worker;
using OpenRAG.Worker.Consumers;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddMediator(options => options.ServiceLifetime = ServiceLifetime.Scoped);

// Register CAP subscribers so CAP can discover them
builder.Services.AddTransient<DocumentUploadedConsumer>();
builder.Services.AddTransient<DocumentPreprocessRequestedConsumer>();
builder.Services.AddTransient<DocumentPreprocessedConsumer>();
builder.Services.AddTransient<DocumentChunkingRequestedConsumer>();
builder.Services.AddTransient<DocumentChunkedConsumer>();
builder.Services.AddTransient<DocumentIntelligenceRequestedConsumer>();
builder.Services.AddTransient<DocumentIntelligenceGeneratedConsumer>();
builder.Services.AddTransient<DocumentEmbeddingsRequestedConsumer>();

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
