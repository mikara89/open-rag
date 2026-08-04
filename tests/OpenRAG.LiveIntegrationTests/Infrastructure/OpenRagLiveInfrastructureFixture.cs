using System.Net;
using System.Security.Cryptography;
using System.Text;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Npgsql;
using OpenRAG.Application.Abstractions.Persistence;
using OpenRAG.Application.Abstractions.Storage;
using OpenRAG.Domain.Documents;
using OpenRAG.Domain.Processing;
using OpenRAG.Infrastructure.Persistence;
using OpenRAG.Worker;
using Pgvector.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace OpenRAG.LiveIntegrationTests.Infrastructure;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class OpenRagLiveInfrastructureTestGroup
    : ICollectionFixture<OpenRagLiveInfrastructureFixture>
{
    public const string Name = "OpenRAG live infrastructure";
}

public sealed class OpenRagLiveInfrastructureFixture : IAsyncLifetime
{
    private static readonly string[] ApplicationTables =
    [
        "document_processing_steps",
        "document_processing_runs",
        "document_intelligence",
        "document_embeddings",
        "document_chunks",
        "document_versions",
        "documents"
    ];

    private readonly PostgreSqlContainer _postgres;
    private readonly string _runRoot;
    private readonly string _diagnosticsRoot;
    private ServiceProvider? _workerProvider;

    public OpenRagLiveInfrastructureFixture()
    {
        var repositoryRoot = FindRepositoryRoot();
        var runId = Guid.NewGuid().ToString("N");
        _runRoot = Path.Combine(repositoryRoot, "artifacts", "live-tests", runId);
        _diagnosticsRoot = Path.Combine(repositoryRoot, "artifacts", "live-test-diagnostics");
        StorageRoot = Path.Combine(_runRoot, "storage");
        Directory.CreateDirectory(StorageRoot);
        Directory.CreateDirectory(_diagnosticsRoot);

        _postgres = new PostgreSqlBuilder(LiveTestConstants.PostgreSqlImage)
            .WithDatabase($"openrag_live_{runId[..12]}")
            .WithUsername("openrag_live")
            .WithPassword($"live-{Guid.NewGuid():N}")
            .WithCleanUp(true)
            .Build();
    }

    public string StorageRoot { get; }
    public string ConnectionString => _postgres.GetConnectionString();
    internal LiveProviderProbe ProviderProbe { get; } = new();
    internal CapturingDocumentEventBus EventBus { get; } = new();
    internal LiveApiFactory ApiFactory { get; private set; } = null!;
    internal IServiceProvider WorkerProvider => _workerProvider
        ?? throw new InvalidOperationException("The Worker provider has not been initialized.");
    public string PostgreSqlServerVersion { get; private set; } = string.Empty;
    public string PgvectorExtensionVersion { get; private set; } = string.Empty;
    public IReadOnlyList<string> AppliedMigrations { get; private set; } = [];

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var dbContext = CreateDbContext();
        Assert.True(await dbContext.Database.CanConnectAsync());
        await dbContext.Database.MigrateAsync();

        var pending = (await dbContext.Database.GetPendingMigrationsAsync()).ToArray();
        Assert.Empty(pending);
        AppliedMigrations = (await dbContext.Database.GetAppliedMigrationsAsync()).ToArray();
        Assert.NotEmpty(AppliedMigrations);
        PostgreSqlServerVersion = await dbContext.Database
            .SqlQueryRaw<string>("SELECT current_setting('server_version') AS \"Value\"")
            .SingleAsync();
        PgvectorExtensionVersion = await dbContext.Database
            .SqlQueryRaw<string>("SELECT extversion AS \"Value\" FROM pg_extension WHERE extname = 'vector'")
            .SingleAsync();

        var configurationValues = CreateConfigurationValues();
        ApiFactory = new LiveApiFactory(configurationValues, ProviderProbe, EventBus);
        _ = ApiFactory.Services;
        _workerProvider = BuildWorkerProvider(configurationValues);

        using var client = CreateTenantAClient();
        using var response = await client.GetAsync("/api/documents");
        var smokeBody = await response.Content.ReadAsStringAsync();
        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Authenticated API smoke test returned {(int)response.StatusCode}: {smokeBody}");
    }

    public async Task DisposeAsync()
    {
        await WriteSafeDiagnosticsAsync();
        if (_workerProvider is not null)
            await _workerProvider.DisposeAsync();
        if (ApiFactory is not null)
            await ApiFactory.DisposeAsync();
        await _postgres.DisposeAsync();

        if (Directory.Exists(_runRoot))
            Directory.Delete(_runRoot, recursive: true);
    }

    public AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ConnectionString, npgsql => npgsql.UseVector())
            .Options;
        return new AppDbContext(options);
    }

    public IServiceScope CreateWorkerScope() => WorkerProvider.CreateScope();
    internal ServiceProvider CreateWorkerProvider(string connectionString)
    {
        var values = new Dictionary<string, string?>(CreateConfigurationValues())
        {
            ["ConnectionStrings:openrag-db"] = connectionString
        };
        return BuildWorkerProvider(values);
    }
    public HttpClient CreateTenantAClient() =>
        ApiFactory.CreateAuthenticatedClient(LiveTestConstants.UserA, LiveTestConstants.TenantA);
    public HttpClient CreateTenantBClient() =>
        ApiFactory.CreateAuthenticatedClient(LiveTestConstants.UserB, LiveTestConstants.TenantB);
    public HttpClient CreateAdminTenantAClient() =>
        ApiFactory.CreateAuthenticatedClient(LiveTestConstants.UserA, LiveTestConstants.TenantA, true);
    public HttpClient CreateAdminTenantBClient() =>
        ApiFactory.CreateAuthenticatedClient(LiveTestConstants.UserB, LiveTestConstants.TenantB, true);

    public async Task ResetAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        var tables = string.Join(", ", ApplicationTables.Select(table => $"\"{table}\""));
        await using var command = new NpgsqlCommand($"TRUNCATE TABLE {tables} RESTART IDENTITY CASCADE", connection);
        await command.ExecuteNonQueryAsync();

        if (Directory.Exists(StorageRoot))
        {
            foreach (var directory in Directory.EnumerateDirectories(StorageRoot))
                Directory.Delete(directory, recursive: true);
            foreach (var file in Directory.EnumerateFiles(StorageRoot))
                File.Delete(file);
        }
        else
        {
            Directory.CreateDirectory(StorageRoot);
        }

        ProviderProbe.Reset();
        EventBus.Reset();
    }

    internal async Task<SeededDocument> SeedDocumentAsync(
        LiveTestDocumentSeed seed,
        CancellationToken cancellationToken = default)
    {
        using var scope = ApiFactory.Services.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
        var objectKeys = scope.ServiceProvider.GetRequiredService<IDocumentObjectKeyPolicy>();
        var documents = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
        var chunks = scope.ServiceProvider.GetRequiredService<IDocumentChunkRepository>();
        var embeddings = scope.ServiceProvider.GetRequiredService<IDocumentEmbeddingRepository>();
        var intelligence = scope.ServiceProvider.GetRequiredService<IDocumentIntelligenceRepository>();
        var processing = scope.ServiceProvider.GetRequiredService<IProcessingRunRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var sourceKey = objectKeys.BuildSourceKey(
            seed.TenantId,
            seed.DocumentId,
            seed.VersionId,
            seed.FileName);
        var markdownKey = objectKeys.BuildArtifactKey(
            seed.TenantId,
            seed.DocumentId,
            seed.VersionId,
            DocumentObjectKind.Markdown);
        var jsonKey = objectKeys.BuildArtifactKey(
            seed.TenantId,
            seed.DocumentId,
            seed.VersionId,
            DocumentObjectKind.Json);

        var sourceBytes = Encoding.UTF8.GetBytes($"Source {seed.Marker}");
        await using var source = new MemoryStream(sourceBytes);
        var storedSource = await storage.SaveAsync(
            source,
            sourceKey,
            "text/plain",
            cancellationToken);
        await SaveTextAsync(storage, markdownKey, $"# Live document\n\n{seed.Marker}", "text/markdown", cancellationToken);
        await SaveTextAsync(storage, jsonKey, $"{{\"marker\":\"{seed.Marker}\"}}", "application/json", cancellationToken);

        var document = Document.Create(
            seed.DocumentId,
            seed.TenantId,
            seed.Title,
            seed.FileName,
            seed.UserId);
        var version = document.AddVersion(
            seed.VersionId,
            1,
            sourceKey,
            "text/plain",
            storedSource.SizeBytes,
            storedSource.Sha256 ?? throw new InvalidOperationException("Local storage did not return a SHA-256 hash."));
        version.AttachDoclingArtifacts(markdownKey, jsonKey);
        version.MarkPreprocessed();
        if (seed.Status is DocumentStatus.Processing or DocumentStatus.Ready)
            document.MarkProcessing();
        if (seed.Status == DocumentStatus.Ready)
            document.MarkReady();
        if (seed.Status == DocumentStatus.Failed)
            document.MarkFailed();
        if (seed.Status == DocumentStatus.Deleted)
            document.SoftDelete();

        var chunk = DocumentChunk.Create(
            seed.ChunkId,
            seed.TenantId,
            seed.DocumentId,
            seed.VersionId,
            0,
            seed.Marker,
            Sha256(seed.Marker),
            3,
            1,
            "Live isolation");
        var embedding = DocumentEmbedding.Create(
            seed.EmbeddingId,
            seed.TenantId,
            seed.DocumentId,
            seed.VersionId,
            seed.ChunkId,
            seed.Vector,
            seed.EmbeddingProvider,
            seed.EmbeddingModel,
            seed.Vector.Length,
            seed.EmbeddingVersion);
        var intelligenceRecord = DocumentIntelligence.Create(
            seed.IntelligenceId,
            seed.TenantId,
            seed.DocumentId,
            seed.VersionId,
            "live-test",
            $"Summary {seed.Marker}",
            "[]",
            "[]",
            "{}",
            "live-deterministic",
            "live-intelligence");
        var run = DocumentProcessingRun.Create(
            seed.RunId,
            seed.TenantId,
            seed.DocumentId,
            seed.VersionId,
            ProcessingRunReason.InitialUpload,
            $"seed-{seed.DocumentId:N}");
        run.Start();
        run.MarkCompleted();
        var step = DocumentProcessingStep.Create(
            seed.StepId,
            seed.TenantId,
            seed.DocumentId,
            seed.VersionId,
            seed.RunId,
            DocumentProcessingStepName.Preprocess,
            3,
            Sha256("input"),
            "live-seed",
            "v1");
        step.Start();
        step.MarkCompleted(Sha256("output"));

        await documents.AddAsync(document, cancellationToken);
        await chunks.AddRangeAsync([chunk], cancellationToken);
        await embeddings.AddRangeAsync([embedding], cancellationToken);
        await intelligence.AddAsync(intelligenceRecord, cancellationToken);
        await processing.AddAsync(run, cancellationToken);
        await processing.AddStepAsync(step, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new SeededDocument(document, version, chunk, embedding, intelligenceRecord, run, step);
    }

    internal async Task<DocumentProcessingRun> CreateRunningProcessingRunAsync(
        Guid tenantId,
        Guid documentId,
        Guid versionId,
        Guid runId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        using var scope = ApiFactory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IProcessingRunRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var run = DocumentProcessingRun.Create(
            runId,
            tenantId,
            documentId,
            versionId,
            ProcessingRunReason.ManualRetry,
            correlationId);
        run.Start();
        await repository.AddAsync(run, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return run;
    }

    internal async Task<IReadOnlyList<StorageManifestEntry>> GetStorageManifestAsync()
    {
        if (!Directory.Exists(StorageRoot))
            return [];

        var entries = new List<StorageManifestEntry>();
        foreach (var file in Directory.EnumerateFiles(StorageRoot, "*", SearchOption.AllDirectories))
        {
            await using var stream = File.OpenRead(file);
            var hash = await SHA256.HashDataAsync(stream);
            entries.Add(new StorageManifestEntry(
                Path.GetRelativePath(StorageRoot, file).Replace('\\', '/'),
                stream.Length,
                Convert.ToHexStringLower(hash)));
        }
        return entries.OrderBy(entry => entry.RelativePath, StringComparer.Ordinal).ToArray();
    }

    private IReadOnlyDictionary<string, string?> CreateConfigurationValues() =>
        new Dictionary<string, string?>
        {
            ["ConnectionStrings:openrag-db"] = ConnectionString,
            ["ConnectionStrings:rabbitmq"] = "amqp://guest:guest@localhost:5672/",
            ["Storage:Provider"] = "Local",
            ["Storage:LocalRootPath"] = StorageRoot,
            ["Preprocessing:Docling:Provider"] = "Mock",
            ["Chunking:Provider"] = "SimpleMarkdown",
            ["AI:Embeddings:Provider"] = "Mock",
            ["AI:Embeddings:Model"] = LiveTestConstants.EmbeddingModel,
            ["AI:Embeddings:Dimensions"] = LiveTestConstants.EmbeddingDimensions.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            ["AI:Chat:Provider"] = "Mock",
            ["Intelligence:Provider"] = "Mock",
            ["Rag:TopK"] = "5",
            [$"Authentication:Jwt:Authority"] = LiveApiFactory.Issuer,
            [$"Authentication:Jwt:Audience"] = LiveApiFactory.Audience,
            [$"Authentication:Jwt:RequireHttpsMetadata"] = "true",
            [$"Authentication:Jwt:UserIdClaimType"] = "sub",
            [$"Authentication:Jwt:TenantIdClaimType"] = "tenant_id",
            [$"Authentication:Jwt:RoleClaimType"] = "role",
            [$"Authentication:Jwt:ClockSkewSeconds"] = "60"
        };

    private ServiceProvider BuildWorkerProvider(IReadOnlyDictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddOpenRagWorkerApplication(configuration);
        LiveApiFactory.ReplaceExternalBoundaries(services, ProviderProbe, EventBus);
        services.RemoveAll<IHostedService>();
        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = false
        });
    }

    private async Task WriteSafeDiagnosticsAsync()
    {
        try
        {
            var lines = new List<string>
            {
                $"image={LiveTestConstants.PostgreSqlImage}",
                $"container.state={_postgres.State}",
                $"postgresql={PostgreSqlServerVersion}",
                $"pgvector={PgvectorExtensionVersion}",
                $"migrations={string.Join(',', AppliedMigrations)}"
            };
            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync();
            foreach (var table in ApplicationTables)
            {
                await using var command = new NpgsqlCommand(
                    $"SELECT COUNT(*) FROM \"{table}\"",
                    connection);
                var count = Convert.ToInt64(
                    await command.ExecuteScalarAsync(),
                    System.Globalization.CultureInfo.InvariantCulture);
                lines.Add($"rows.{table}={count}");
            }
            foreach (var entry in await GetStorageManifestAsync())
                lines.Add($"storage={entry.RelativePath}|{entry.Length}|{entry.Sha256}");

            var path = Path.Combine(_diagnosticsRoot, "live-test-safe-diagnostics.txt");
            await File.WriteAllLinesAsync(path, lines);

            var (standardOutput, standardError) = await _postgres.GetLogsAsync(
                DateTime.UnixEpoch,
                DateTime.UtcNow,
                timestampsEnabled: true,
                CancellationToken.None);
            await File.WriteAllTextAsync(
                Path.Combine(_diagnosticsRoot, "postgresql-container.log"),
                standardOutput + Environment.NewLine + standardError);
        }
        catch (Exception exception)
        {
            var path = Path.Combine(_diagnosticsRoot, "live-test-diagnostics-error.txt");
            await File.WriteAllTextAsync(path, exception.GetType().FullName ?? "diagnostic failure");
        }
    }

    private static async Task SaveTextAsync(
        IFileStorage storage,
        string key,
        string value,
        string contentType,
        CancellationToken cancellationToken)
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(value));
        await storage.SaveAsync(stream, key, contentType, cancellationToken);
    }

    private static string Sha256(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "OpenRAG.slnx")))
                return current.FullName;
            current = current.Parent;
        }
        throw new InvalidOperationException("Could not locate the OpenRAG repository root.");
    }
}

internal sealed record LiveTestDocumentSeed(
    Guid TenantId,
    Guid UserId,
    Guid DocumentId,
    Guid VersionId,
    Guid ChunkId,
    Guid EmbeddingId,
    Guid IntelligenceId,
    Guid RunId,
    Guid StepId,
    string Title,
    string FileName,
    string Marker,
    float[] Vector,
    DocumentStatus Status = DocumentStatus.Ready,
    string EmbeddingProvider = LiveTestConstants.EmbeddingProvider,
    string EmbeddingModel = LiveTestConstants.EmbeddingModel,
    string EmbeddingVersion = LiveTestConstants.EmbeddingVersion);

internal sealed record SeededDocument(
    Document Document,
    DocumentVersion Version,
    DocumentChunk Chunk,
    DocumentEmbedding Embedding,
    DocumentIntelligence Intelligence,
    DocumentProcessingRun Run,
    DocumentProcessingStep Step);

internal sealed record StorageManifestEntry(string RelativePath, long Length, string Sha256);
