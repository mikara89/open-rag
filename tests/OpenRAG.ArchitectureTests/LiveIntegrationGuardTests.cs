namespace OpenRAG.ArchitectureTests;

public sealed class LiveIntegrationGuardTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly string LiveTestsRoot = Path.Combine(
        RepositoryRoot,
        "tests",
        "OpenRAG.LiveIntegrationTests");

    [Fact]
    public void Live_project_references_real_infrastructure_and_only_one_container_package()
    {
        var project = File.ReadAllText(Path.Combine(
            LiveTestsRoot,
            "OpenRAG.LiveIntegrationTests.csproj"));

        Assert.Contains("OpenRAG.Infrastructure.csproj", project, StringComparison.Ordinal);
        Assert.Contains("Testcontainers.PostgreSql", project, StringComparison.Ordinal);
        Assert.Equal(1, Count(project, "Testcontainers."));
    }

    [Fact]
    public void Live_tests_do_not_use_non_relational_database_substitutes_or_fake_repositories()
    {
        var source = ReadAllCSharp(LiveTestsRoot);

        Assert.DoesNotContain("UseInMemoryDatabase", source, StringComparison.Ordinal);
        Assert.DoesNotContain("UseSqlite", source, StringComparison.Ordinal);
        Assert.DoesNotContain("FakeDocumentRepository", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MockDocumentRepository", source, StringComparison.Ordinal);
        Assert.DoesNotContain("FakeProcessingRunRepository", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MockProcessingRunRepository", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Live_vector_and_worker_tests_require_production_composition()
    {
        var vectorTest = File.ReadAllText(Path.Combine(
            LiveTestsRoot,
            "Vector",
            "LivePgvectorIsolationTests.cs"));
        var workerTest = File.ReadAllText(Path.Combine(
            LiveTestsRoot,
            "Worker",
            "LiveWorkerIsolationTests.cs"));

        Assert.Contains("Assert.IsType<EfVectorSearchService>", vectorTest, StringComparison.Ordinal);
        Assert.Contains("AddOpenRagWorkerApplication", ReadAllCSharp(LiveTestsRoot), StringComparison.Ordinal);
        Assert.DoesNotContain("HttpContextCurrentTenant", workerTest, StringComparison.Ordinal);
    }

    [Fact]
    public void Production_projects_do_not_reference_testcontainers()
    {
        var productionFiles = Directory.EnumerateFiles(
            Path.Combine(RepositoryRoot, "src"),
            "*",
            SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase));

        Assert.DoesNotContain(productionFiles, path =>
            File.ReadAllText(path).Contains("Testcontainers", StringComparison.Ordinal));
    }

    [Fact]
    public void Api_routes_do_not_accept_tenant_overrides()
    {
        var apiSource = ReadAllCSharp(Path.Combine(RepositoryRoot, "src", "OpenRAG.Api"));

        Assert.DoesNotContain("X-Tenant-Id", apiSource, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/{tenantId", apiSource, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("{tenantId}", apiSource, StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadAllCSharp(string root) => string.Join(
        Environment.NewLine,
        Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(File.ReadAllText));

    private static int Count(string value, string fragment)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(fragment, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += fragment.Length;
        }
        return count;
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "OpenRAG.slnx")))
                return current.FullName;
            current = current.Parent;
        }
        throw new InvalidOperationException("Could not locate the OpenRAG repository root.");
    }
}
