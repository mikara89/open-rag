#pragma warning disable ASPIREPROCESSCOMMAND001

using Aspire.Hosting.ApplicationModel;

var builder = DistributedApplication.CreateBuilder(args);

var username = builder.AddParameter("username", secret: true);
var password = builder.AddParameter("password", secret: true);

var postgres = builder.AddPostgres("postgres", username, password)
    .WithImage("pgvector/pgvector", "pg17")
    .WithLifetime(ContainerLifetime.Persistent)
    .WithDataVolume();

var openRagDb = postgres.AddDatabase("openrag-db", databaseName: "openrag");

// Adjust this path if your API project folder/csproj name is different.
var apiProject = Path.GetFullPath(Path.Combine(
    builder.AppHostDirectory,
    "..",
    "OpenRAG.Api",
    "OpenRAG.Api.csproj"));

openRagDb.WithProcessCommand(
    commandName: "run-latest-migration",
    displayName: "Run latest migration",
    processSpecFactory: context =>
    {
        var connectionString = openRagDb.Resource.ConnectionStringExpression
            .GetValueAsync(context.CancellationToken)
            .GetAwaiter()
            .GetResult();

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Could not resolve openrag-db connection string.");
        }

        return new ProcessCommandSpec("dotnet")
        {
            Arguments =
            [
                "ef",
                "database",
                "update",
                "--project",
                apiProject,
                "--startup-project",
                apiProject,
                "--connection",
                connectionString
            ],
            WorkingDirectory = builder.AppHostDirectory,
            EnvironmentVariables =
            {
                ["ASPNETCORE_ENVIRONMENT"] = "Development"
            }
        };
    },
    commandOptions: new ProcessCommandOptions
    {
        MaxOutputLineCount = 300,
        DisplayImmediately = true
    });

var rabbitmq = builder.AddRabbitMQ("rabbitmq")
    .WithLifetime(ContainerLifetime.Persistent)
    .WithManagementPlugin();

// Docling Serve for real document preprocessing
var docling = builder.AddContainer("docling-serve", "quay.io/docling-project/docling-serve")
    .WithHttpEndpoint(port: 5001, targetPort: 5001, name: "http")
    .WithEnvironment("DOCLING_SERVE_ENABLE_UI", "1")
    .WithLifetime(ContainerLifetime.Persistent);

builder.AddProject<Projects.OpenRAG_Api>("openrag-api")
    .WithReference(openRagDb)
    .WithReference(rabbitmq)
    .WithEnvironment("DOCLING_BASE_URL", docling.Resource.GetEndpoint("http"))
    .WaitFor(openRagDb)
    .WaitFor(rabbitmq);

builder.AddProject<Projects.OpenRAG_Worker>("openrag-worker")
    .WithReference(openRagDb)
    .WithReference(rabbitmq)
    .WithEnvironment("DOCLING_BASE_URL", docling.Resource.GetEndpoint("http"))
    .WaitFor(openRagDb)
    .WaitFor(rabbitmq);

builder.Build().Run();