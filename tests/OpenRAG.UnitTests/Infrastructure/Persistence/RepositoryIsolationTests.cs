using Microsoft.EntityFrameworkCore;
using OpenRAG.Application.Common;
using OpenRAG.Domain.Documents;
using OpenRAG.Infrastructure.Persistence;
using OpenRAG.Infrastructure.Persistence.Repositories;
using Pgvector.EntityFrameworkCore;

namespace OpenRAG.UnitTests.Infrastructure.Persistence;

public sealed class RepositoryIsolationTests
{
    private static readonly Guid TenantA = new("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TenantB = new("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid DocumentId = Guid.NewGuid();
    private static readonly Guid VersionId = Guid.NewGuid();

    [Fact]
    public async Task Mixed_tenant_chunk_batch_is_rejected_before_tracking()
    {
        using var context = CreateContext();
        var repository = new EfDocumentChunkRepository(context);
        DocumentChunk[] chunks =
        [
            CreateChunk(TenantA, 0),
            CreateChunk(TenantB, 1)
        ];

        await Assert.ThrowsAsync<IsolationViolationException>(() =>
            repository.AddRangeAsync(chunks));

        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Mixed_document_embedding_batch_is_rejected_before_tracking()
    {
        using var context = CreateContext();
        var repository = new EfDocumentEmbeddingRepository(context);
        DocumentEmbedding[] embeddings =
        [
            CreateEmbedding(DocumentId),
            CreateEmbedding(Guid.NewGuid())
        ];

        await Assert.ThrowsAsync<IsolationViolationException>(() =>
            repository.AddRangeAsync(embeddings));

        Assert.Empty(context.ChangeTracker.Entries());
    }

    private static DocumentChunk CreateChunk(Guid tenantId, int index) =>
        DocumentChunk.Create(
            Guid.NewGuid(), tenantId, DocumentId, VersionId, index,
            $"content-{index}", $"hash-{index}", 1);

    private static DocumentEmbedding CreateEmbedding(Guid documentId) =>
        DocumentEmbedding.Create(
            Guid.NewGuid(), TenantA, documentId, VersionId, Guid.NewGuid(),
            [0.1f, 0.2f], "provider", "model", 2, "v1");

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=openrag_repository_test;Username=test;Password=test",
                npgsql => npgsql.UseVector())
            .Options;
        return new AppDbContext(options);
    }
}
