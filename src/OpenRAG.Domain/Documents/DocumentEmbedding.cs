using OpenRAG.Domain.Common;

namespace OpenRAG.Domain.Documents;

public sealed class DocumentEmbedding : Entity
{
    public Guid TenantId { get; private set; }
    public Guid DocumentId { get; private set; }
    public Guid VersionId { get; private set; }
    public Guid ChunkId { get; private set; }
    public float[] Vector { get; private set; }
    public string EmbeddingProvider { get; private set; }
    public string EmbeddingModel { get; private set; }
    public int EmbeddingDimensions { get; private set; }
    public string EmbeddingVersion { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private DocumentEmbedding(
        Guid id,
        Guid tenantId,
        Guid documentId,
        Guid versionId,
        Guid chunkId,
        float[] vector,
        string embeddingProvider,
        string embeddingModel,
        int embeddingDimensions,
        string embeddingVersion)
        : base(id)
    {
        TenantId = GuardNotEmpty(tenantId, nameof(TenantId));
        DocumentId = GuardNotEmpty(documentId, nameof(DocumentId));
        VersionId = GuardNotEmpty(versionId, nameof(VersionId));
        ChunkId = GuardNotEmpty(chunkId, nameof(ChunkId));
        Vector = GuardNotEmpty(vector, nameof(Vector));
        EmbeddingProvider = GuardNotEmpty(embeddingProvider, nameof(EmbeddingProvider));
        EmbeddingModel = GuardNotEmpty(embeddingModel, nameof(EmbeddingModel));

        if (embeddingDimensions <= 0)
            throw new DomainException($"{nameof(EmbeddingDimensions)} must be > 0.");

        if (embeddingDimensions != vector.Length)
            throw new DomainException(
                $"{nameof(EmbeddingDimensions)} ({embeddingDimensions}) must match vector length ({vector.Length}).");

        EmbeddingDimensions = embeddingDimensions;
        EmbeddingVersion = GuardNotEmpty(embeddingVersion, nameof(EmbeddingVersion));
        CreatedAt = DateTimeOffset.UtcNow;
    }

    private DocumentEmbedding() { } // EF Core

    public static DocumentEmbedding Create(
        Guid id,
        Guid tenantId,
        Guid documentId,
        Guid versionId,
        Guid chunkId,
        float[] vector,
        string embeddingProvider,
        string embeddingModel,
        int embeddingDimensions,
        string embeddingVersion)
    {
        return new DocumentEmbedding(
            id, tenantId, documentId, versionId, chunkId,
            vector, embeddingProvider, embeddingModel,
            embeddingDimensions, embeddingVersion);
    }

    private static string GuardNotEmpty(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException($"{paramName} cannot be empty.");
        }

        return value;
    }

    private static Guid GuardNotEmpty(Guid value, string paramName)
    {
        if (value == Guid.Empty)
        {
            throw new DomainException($"{paramName} cannot be empty.");
        }

        return value;
    }

    private static float[] GuardNotEmpty(float[] value, string paramName)
    {
        if (value is null || value.Length == 0)
        {
            throw new DomainException($"{paramName} cannot be empty.");
        }

        return value;
    }
}
