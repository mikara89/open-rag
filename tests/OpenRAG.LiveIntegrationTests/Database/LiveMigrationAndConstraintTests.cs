using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OpenRAG.Application.Abstractions.Vector;
using OpenRAG.Domain.Documents;
using OpenRAG.Domain.Processing;
using OpenRAG.Infrastructure.Persistence;
using OpenRAG.Infrastructure.VectorSearch;
using OpenRAG.LiveIntegrationTests.Infrastructure;
using Pgvector.EntityFrameworkCore;

namespace OpenRAG.LiveIntegrationTests.Database;

[Collection(OpenRagLiveInfrastructureTestGroup.Name)]
[Trait("Category", "LiveIntegration")]
[Trait("Security", "CrossTenant")]
public sealed class LiveMigrationAndConstraintTests
{
    private readonly OpenRagLiveInfrastructureFixture _fixture;

    public LiveMigrationAndConstraintTests(OpenRagLiveInfrastructureFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Empty_database_applies_all_production_migrations_and_starts_application()
    {
        await using var context = _fixture.CreateDbContext();

        Assert.StartsWith("17.", _fixture.PostgreSqlServerVersion, StringComparison.Ordinal);
        Assert.Equal(LiveTestConstants.PgvectorVersion, _fixture.PgvectorExtensionVersion);
        Assert.NotEmpty(_fixture.AppliedMigrations);
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.False(context.Database.HasPendingModelChanges());

        var tableCount = await context.Database.SqlQueryRaw<int>("""
            SELECT COUNT(*)::integer AS "Value"
            FROM information_schema.tables
            WHERE table_schema = 'public'
              AND table_name = ANY(ARRAY[
                'documents', 'document_versions', 'document_chunks',
                'document_embeddings', 'document_intelligence',
                'document_processing_runs', 'document_processing_steps'])
            """).SingleAsync();
        Assert.Equal(7, tableCount);

        using var scope = _fixture.ApiFactory.Services.CreateScope();
        Assert.IsType<AppDbContext>(scope.ServiceProvider.GetRequiredService<AppDbContext>());
        Assert.IsType<EfVectorSearchService>(
            scope.ServiceProvider.GetRequiredService<IVectorSearchService>());
    }

    [Fact]
    public async Task Pgvector_migration_preserves_legacy_little_endian_float_bytes()
    {
        var databaseName = $"openrag_vector_migration_{Guid.NewGuid():N}";
        var liveConnection = new NpgsqlConnectionStringBuilder(_fixture.ConnectionString);
        var administrationConnection = new NpgsqlConnectionStringBuilder(liveConnection.ConnectionString)
        {
            Database = "postgres"
        };
        var migrationConnection = new NpgsqlConnectionStringBuilder(liveConnection.ConnectionString)
        {
            Database = databaseName
        };

        await using (var connection = new NpgsqlConnection(administrationConnection.ConnectionString))
        {
            await connection.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", connection);
            await create.ExecuteNonQueryAsync();
        }

        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(migrationConnection.ConnectionString, npgsql => npgsql.UseVector())
                .Options;
            await using var context = new AppDbContext(options);
            await context.Database.MigrateAsync("20260619065140_ExpandProcessingStepHashColumns");

            var vectorBytes = new[] { 1f, 0.5f, -2f }
                .SelectMany(BitConverter.GetBytes)
                .ToArray();
            await using (var connection = new NpgsqlConnection(migrationConnection.ConnectionString))
            {
                await connection.OpenAsync();
                await using var insert = new NpgsqlCommand("""
                    INSERT INTO document_embeddings
                        ("Id", "TenantId", "DocumentId", "VersionId", "ChunkId", "Vector",
                         "EmbeddingProvider", "EmbeddingModel", "EmbeddingDimensions",
                         "EmbeddingVersion", "CreatedAt")
                    VALUES
                        (@id, @tenant, @document, @version, @chunk, @vector,
                         'legacy', 'legacy', 3, 'v1', now())
                    """, connection);
                insert.Parameters.AddWithValue("id", Guid.NewGuid());
                insert.Parameters.AddWithValue("tenant", LiveTestConstants.TenantA);
                insert.Parameters.AddWithValue("document", Guid.NewGuid());
                insert.Parameters.AddWithValue("version", Guid.NewGuid());
                insert.Parameters.AddWithValue("chunk", Guid.NewGuid());
                insert.Parameters.AddWithValue("vector", vectorBytes);
                await insert.ExecuteNonQueryAsync();
            }

            await context.Database.MigrateAsync("20260620160521_MigrateEmbeddingVectorToPgvector");
            var migrated = await context.Database.SqlQueryRaw<string>(
                "SELECT \"Vector\"::text AS \"Value\" FROM document_embeddings")
                .SingleAsync();
            Assert.Equal("[1,0.5,-2]", migrated);

            await context.Database.MigrateAsync("20260619065140_ExpandProcessingStepHashColumns");
            var rolledBack = await context.Database.SqlQueryRaw<string>(
                "SELECT encode(\"Vector\", 'hex') AS \"Value\" FROM document_embeddings")
                .SingleAsync();
            Assert.Equal(Convert.ToHexStringLower(vectorBytes), rolledBack);
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await using var connection = new NpgsqlConnection(administrationConnection.ConnectionString);
            await connection.OpenAsync();
            await using var drop = new NpgsqlCommand(
                $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)",
                connection);
            await drop.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task Required_tenant_inclusive_foreign_keys_exist()
    {
        await using var context = _fixture.CreateDbContext();
        var constraints = await context.Database.SqlQueryRaw<string>("""
            SELECT child.relname || '|' || parent.relname || '|'
                || array_to_string(ARRAY(
                    SELECT attribute.attname
                    FROM unnest(constraint_row.conkey) WITH ORDINALITY AS key(attnum, position)
                    JOIN pg_attribute attribute
                      ON attribute.attrelid = constraint_row.conrelid
                     AND attribute.attnum = key.attnum
                    ORDER BY key.position), ',') || '|'
                || array_to_string(ARRAY(
                    SELECT attribute.attname
                    FROM unnest(constraint_row.confkey) WITH ORDINALITY AS key(attnum, position)
                    JOIN pg_attribute attribute
                      ON attribute.attrelid = constraint_row.confrelid
                     AND attribute.attnum = key.attnum
                    ORDER BY key.position), ',') AS "Value"
            FROM pg_constraint constraint_row
            JOIN pg_class child ON child.oid = constraint_row.conrelid
            JOIN pg_class parent ON parent.oid = constraint_row.confrelid
            WHERE constraint_row.contype = 'f'
            ORDER BY child.relname, parent.relname
            """).ToListAsync();

        Assert.Contains("document_versions|documents|TenantId,DocumentId|TenantId,Id", constraints);
        Assert.Contains("document_chunks|document_versions|TenantId,DocumentId,VersionId|TenantId,DocumentId,Id", constraints);
        Assert.Contains("document_embeddings|document_chunks|TenantId,DocumentId,VersionId,ChunkId|TenantId,DocumentId,VersionId,Id", constraints);
        Assert.Contains("document_intelligence|document_versions|TenantId,DocumentId,VersionId|TenantId,DocumentId,Id", constraints);
        Assert.Contains("document_processing_runs|document_versions|TenantId,DocumentId,VersionId|TenantId,DocumentId,Id", constraints);
        Assert.Contains("document_processing_steps|document_processing_runs|TenantId,DocumentId,VersionId,ProcessingRunId|TenantId,DocumentId,VersionId,Id", constraints);
    }

    [Theory]
    [InlineData("version", 101)]
    [InlineData("chunk", 102)]
    [InlineData("embedding", 103)]
    [InlineData("intelligence", 104)]
    [InlineData("run", 105)]
    [InlineData("step", 106)]
    public async Task Cross_tenant_relationship_writes_fail_with_foreign_key_violation(
        string relationship,
        int scenario)
    {
        await _fixture.ResetAsync();
        var ids = LiveTestIds.ForScenario(scenario);
        var seeded = await _fixture.SeedDocumentAsync(LiveTestData.TenantA1(ids));
        var invalidId = Guid.NewGuid();

        await using var context = _fixture.CreateDbContext();
        switch (relationship)
        {
            case "version":
                context.DocumentVersions.Add(DocumentVersion.Create(
                    invalidId,
                    LiveTestConstants.TenantB,
                    ids.DocumentA1,
                    2,
                    "tenants/foreign/original.txt",
                    "text/plain",
                    1,
                    "hash"));
                break;
            case "chunk":
                context.DocumentChunks.Add(DocumentChunk.Create(
                    invalidId,
                    LiveTestConstants.TenantB,
                    ids.DocumentA1,
                    ids.VersionA1,
                    1,
                    "foreign",
                    "hash",
                    1));
                break;
            case "embedding":
                context.DocumentEmbeddings.Add(DocumentEmbedding.Create(
                    invalidId,
                    LiveTestConstants.TenantB,
                    ids.DocumentA1,
                    ids.VersionA1,
                    ids.ChunkA1,
                    [0f, 1f, 0f],
                    LiveTestConstants.EmbeddingProvider,
                    LiveTestConstants.EmbeddingModel,
                    3,
                    LiveTestConstants.EmbeddingVersion));
                break;
            case "intelligence":
                context.DocumentIntelligence.Add(DocumentIntelligence.Create(
                    invalidId,
                    LiveTestConstants.TenantB,
                    ids.DocumentA1,
                    ids.VersionA1,
                    null,
                    "foreign",
                    null,
                    null,
                    null,
                    "test",
                    "test"));
                break;
            case "run":
                context.DocumentProcessingRuns.Add(DocumentProcessingRun.Create(
                    invalidId,
                    LiveTestConstants.TenantB,
                    ids.DocumentA1,
                    ids.VersionA1,
                    ProcessingRunReason.ManualRetry,
                    "foreign-run"));
                break;
            case "step":
                context.DocumentProcessingSteps.Add(DocumentProcessingStep.Create(
                    invalidId,
                    LiveTestConstants.TenantB,
                    ids.DocumentA1,
                    ids.VersionA1,
                    seeded.Run.Id,
                    DocumentProcessingStepName.Preprocess,
                    1,
                    "input",
                    "test",
                    "v1"));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(relationship));
        }

        PostgresException postgres;
        if (relationship == "embedding")
        {
            context.ChangeTracker.Clear();
            await using var connection = new NpgsqlConnection(_fixture.ConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("""
                INSERT INTO document_embeddings
                    ("Id", "TenantId", "DocumentId", "VersionId", "ChunkId", "Vector",
                     "EmbeddingProvider", "EmbeddingModel", "EmbeddingDimensions",
                     "EmbeddingVersion", "CreatedAt")
                VALUES
                    (@id, @tenant, @document, @version, @chunk, '[0,1,0]'::vector,
                     @provider, @model, 3, @embeddingVersion, now())
                """, connection);
            command.Parameters.AddWithValue("id", invalidId);
            command.Parameters.AddWithValue("tenant", LiveTestConstants.TenantB);
            command.Parameters.AddWithValue("document", ids.DocumentA1);
            command.Parameters.AddWithValue("version", ids.VersionA1);
            command.Parameters.AddWithValue("chunk", ids.ChunkA1);
            command.Parameters.AddWithValue("provider", LiveTestConstants.EmbeddingProvider);
            command.Parameters.AddWithValue("model", LiveTestConstants.EmbeddingModel);
            command.Parameters.AddWithValue("embeddingVersion", LiveTestConstants.EmbeddingVersion);
            postgres = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        }
        else
        {
            var exception = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
            postgres = Assert.IsType<PostgresException>(exception.InnerException);
        }
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, postgres.SqlState);

        await using var verification = _fixture.CreateDbContext();
        Assert.False(await ContainsIdAsync(verification, relationship, invalidId));
    }

    private static Task<bool> ContainsIdAsync(
        AppDbContext context,
        string relationship,
        Guid id) =>
        relationship switch
        {
            "version" => context.DocumentVersions.AnyAsync(item => item.Id == id),
            "chunk" => context.DocumentChunks.AnyAsync(item => item.Id == id),
            "embedding" => context.DocumentEmbeddings.AnyAsync(item => item.Id == id),
            "intelligence" => context.DocumentIntelligence.AnyAsync(item => item.Id == id),
            "run" => context.DocumentProcessingRuns.AnyAsync(item => item.Id == id),
            "step" => context.DocumentProcessingSteps.AnyAsync(item => item.Id == id),
            _ => throw new ArgumentOutOfRangeException(nameof(relationship))
        };
}
