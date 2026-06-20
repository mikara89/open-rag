using OpenRAG.Domain.Common;
using OpenRAG.Domain.Documents;

namespace OpenRAG.UnitTests.Domain.Documents;

public sealed class DocumentEmbeddingTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid DocId = Guid.NewGuid();
    private static readonly Guid VerId = Guid.NewGuid();
    private static readonly Guid ChunkId = Guid.NewGuid();

    [Fact]
    public void Rejects_empty_TenantId()
    {
        var ex = Assert.Throws<DomainException>(() =>
            DocumentEmbedding.Create(
                Guid.NewGuid(), Guid.Empty, DocId, VerId, ChunkId,
                new float[] { 1f, 2f, 3f, 4f, 5f, 6f, 7f, 8f },
                "mock", "mock-8", 8, "v1"));
        Assert.Contains("TenantId", ex.Message);
    }

    [Fact]
    public void Rejects_empty_DocumentId()
    {
        var ex = Assert.Throws<DomainException>(() =>
            DocumentEmbedding.Create(
                Guid.NewGuid(), TenantId, Guid.Empty, VerId, ChunkId,
                new float[] { 1f, 2f, 3f, 4f, 5f, 6f, 7f, 8f },
                "mock", "mock-8", 8, "v1"));
        Assert.Contains("DocumentId", ex.Message);
    }

    [Fact]
    public void Rejects_empty_VersionId()
    {
        var ex = Assert.Throws<DomainException>(() =>
            DocumentEmbedding.Create(
                Guid.NewGuid(), TenantId, DocId, Guid.Empty, ChunkId,
                new float[] { 1f, 2f, 3f, 4f, 5f, 6f, 7f, 8f },
                "mock", "mock-8", 8, "v1"));
        Assert.Contains("VersionId", ex.Message);
    }

    [Fact]
    public void Rejects_empty_ChunkId()
    {
        var ex = Assert.Throws<DomainException>(() =>
            DocumentEmbedding.Create(
                Guid.NewGuid(), TenantId, DocId, VerId, Guid.Empty,
                new float[] { 1f, 2f, 3f, 4f, 5f, 6f, 7f, 8f },
                "mock", "mock-8", 8, "v1"));
        Assert.Contains("ChunkId", ex.Message);
    }

    [Fact]
    public void Rejects_empty_vector()
    {
        var ex = Assert.Throws<DomainException>(() =>
            DocumentEmbedding.Create(
                Guid.NewGuid(), TenantId, DocId, VerId, ChunkId,
                Array.Empty<float>(),
                "mock", "mock-8", 8, "v1"));
        Assert.Contains("Vector", ex.Message);
    }

    [Fact]
    public void Rejects_null_vector()
    {
        var ex = Assert.Throws<DomainException>(() =>
            DocumentEmbedding.Create(
                Guid.NewGuid(), TenantId, DocId, VerId, ChunkId,
                null!,
                "mock", "mock-8", 8, "v1"));
        Assert.Contains("Vector", ex.Message);
    }

    [Fact]
    public void Rejects_dimension_mismatch()
    {
        var ex = Assert.Throws<DomainException>(() =>
            DocumentEmbedding.Create(
                Guid.NewGuid(), TenantId, DocId, VerId, ChunkId,
                new float[] { 1f, 2f, 3f }, // 3 elements, but claims 8
                "mock", "mock-8", 8, "v1"));
        Assert.Contains("must match", ex.Message);
    }

    [Fact]
    public void Creates_valid_embedding()
    {
        var vector = new float[] { 1f, 2f, 3f, 4f, 5f, 6f, 7f, 8f };
        var embedding = DocumentEmbedding.Create(
            Guid.NewGuid(), TenantId, DocId, VerId, ChunkId,
            vector, "mock", "mock-8", 8, "v1");

        Assert.Equal(TenantId, embedding.TenantId);
        Assert.Equal(DocId, embedding.DocumentId);
        Assert.Equal(VerId, embedding.VersionId);
        Assert.Equal(ChunkId, embedding.ChunkId);
        Assert.Equal(vector, embedding.Vector);
        Assert.Equal("mock", embedding.EmbeddingProvider);
        Assert.Equal("mock-8", embedding.EmbeddingModel);
        Assert.Equal(8, embedding.EmbeddingDimensions);
        Assert.Equal("v1", embedding.EmbeddingVersion);
    }

    [Fact]
    public void Rejects_empty_EmbeddingProvider()
    {
        var ex = Assert.Throws<DomainException>(() =>
            DocumentEmbedding.Create(
                Guid.NewGuid(), TenantId, DocId, VerId, ChunkId,
                new float[] { 1f, 2f, 3f, 4f, 5f, 6f, 7f, 8f },
                "", "mock-8", 8, "v1"));
        Assert.Contains("EmbeddingProvider", ex.Message);
    }

    [Fact]
    public void Rejects_empty_EmbeddingModel()
    {
        var ex = Assert.Throws<DomainException>(() =>
            DocumentEmbedding.Create(
                Guid.NewGuid(), TenantId, DocId, VerId, ChunkId,
                new float[] { 1f, 2f, 3f, 4f, 5f, 6f, 7f, 8f },
                "mock", "", 8, "v1"));
        Assert.Contains("EmbeddingModel", ex.Message);
    }
}
