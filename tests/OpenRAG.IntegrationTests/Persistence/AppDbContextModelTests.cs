using Microsoft.EntityFrameworkCore;
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
}
