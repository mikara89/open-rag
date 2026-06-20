using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Pgvector.EntityFrameworkCore;

namespace OpenRAG.Infrastructure.Persistence;

/// <summary>
/// Design-time factory for EF Core migrations.
/// Uses a hardcoded local development connection string;
/// runtime connection strings are injected via Aspire or appsettings.
/// </summary>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        // Try Aspire-assigned port first (63294), fallback to default
        optionsBuilder.UseNpgsql(
            "Host=localhost;Port=63294;Database=openrag;Username=postgres",
            npgsqlOptions => npgsqlOptions.UseVector());

        return new AppDbContext(optionsBuilder.Options);
    }
}
