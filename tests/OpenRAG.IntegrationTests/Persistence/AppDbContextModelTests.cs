using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using OpenRAG.Domain.Documents;
using OpenRAG.Domain.Processing;
using OpenRAG.Infrastructure.Persistence;
using Pgvector.EntityFrameworkCore;

namespace OpenRAG.IntegrationTests.Persistence;

public sealed class AppDbContextModelTests
{
    [Fact]
    public void Model_can_be_built_without_database_connection()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=openrag_test;Username=test;Password=test",
                npgsqlOptions => npgsqlOptions.UseVector())
            .Options;

        using var context = new AppDbContext(options);

        // Accessing the model forces OnModelCreating to run.
        var model = context.Model;

        Assert.NotNull(model);

        var entityTypes = model.GetEntityTypes().Select(e => e.ClrType.Name).ToList();

        Assert.Contains("Document", entityTypes);
        Assert.Contains("DocumentVersion", entityTypes);
        Assert.Contains("DocumentProcessingRun", entityTypes);
        Assert.Contains("DocumentProcessingStep", entityTypes);
    }

    [Fact]
    public void Tenant_owned_relationships_use_complete_composite_foreign_keys()
    {
        using var context = CreateContext();
        var model = context.Model;

        AssertForeignKey<DocumentVersion, Document>(model,
            ["TenantId", "DocumentId"], ["TenantId", "Id"]);
        AssertForeignKey<DocumentChunk, DocumentVersion>(model,
            ["TenantId", "DocumentId", "VersionId"],
            ["TenantId", "DocumentId", "Id"]);
        AssertForeignKey<DocumentEmbedding, DocumentChunk>(model,
            ["TenantId", "DocumentId", "VersionId", "ChunkId"],
            ["TenantId", "DocumentId", "VersionId", "Id"]);
        AssertForeignKey<DocumentIntelligence, DocumentVersion>(model,
            ["TenantId", "DocumentId", "VersionId"],
            ["TenantId", "DocumentId", "Id"]);
        AssertForeignKey<DocumentProcessingRun, DocumentVersion>(model,
            ["TenantId", "DocumentId", "VersionId"],
            ["TenantId", "DocumentId", "Id"]);
        AssertForeignKey<DocumentProcessingStep, DocumentProcessingRun>(model,
            ["TenantId", "DocumentId", "VersionId", "ProcessingRunId"],
            ["TenantId", "DocumentId", "VersionId", "Id"]);
    }

    [Fact]
    public void Composite_relationships_cascade_only_within_matching_tenant_identity()
    {
        using var context = CreateContext();
        var tenantForeignKeys = context.Model.GetEntityTypes()
            .SelectMany(entity => entity.GetForeignKeys())
            .Where(foreignKey => foreignKey.Properties.Any(property => property.Name == "TenantId"))
            .ToArray();

        Assert.Equal(6, tenantForeignKeys.Length);
        Assert.All(tenantForeignKeys, foreignKey =>
        {
            Assert.Equal(DeleteBehavior.Cascade, foreignKey.DeleteBehavior);
            Assert.Contains(foreignKey.Properties, property => property.Name == "TenantId");
            Assert.Contains(foreignKey.PrincipalKey.Properties, property => property.Name == "TenantId");
        });
    }

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=openrag_test;Username=test;Password=test",
                npgsqlOptions => npgsqlOptions.UseVector())
            .Options;
        return new AppDbContext(options);
    }

    private static void AssertForeignKey<TDependent, TPrincipal>(
        IModel model,
        string[] dependentProperties,
        string[] principalProperties)
    {
        var dependent = model.FindEntityType(typeof(TDependent));
        Assert.NotNull(dependent);
        var foreignKey = Assert.Single(dependent.GetForeignKeys(),
            candidate => candidate.PrincipalEntityType.ClrType == typeof(TPrincipal));

        Assert.Equal(dependentProperties, foreignKey.Properties.Select(property => property.Name));
        Assert.Equal(principalProperties, foreignKey.PrincipalKey.Properties.Select(property => property.Name));
    }
}
