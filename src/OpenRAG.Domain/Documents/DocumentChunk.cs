using OpenRAG.Domain.Common;

namespace OpenRAG.Domain.Documents;

public sealed class DocumentChunk : Entity
{
    public Guid TenantId { get; private set; }
    public Guid DocumentId { get; private set; }
    public Guid VersionId { get; private set; }
    public int ChunkIndex { get; private set; }
    public int? PageNumber { get; private set; }
    public string? SectionTitle { get; private set; }
    public string Content { get; private set; }
    public string ContentHash { get; private set; }
    public int TokenCount { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private DocumentChunk(
        Guid id,
        Guid tenantId,
        Guid documentId,
        Guid versionId,
        int chunkIndex,
        int? pageNumber,
        string? sectionTitle,
        string content,
        string contentHash,
        int tokenCount)
        : base(id)
    {
        TenantId = GuardNotEmpty(tenantId, nameof(TenantId));
        DocumentId = GuardNotEmpty(documentId, nameof(DocumentId));
        VersionId = GuardNotEmpty(versionId, nameof(VersionId));
        ChunkIndex = GuardNonNegative(chunkIndex, nameof(ChunkIndex));
        PageNumber = pageNumber;
        SectionTitle = sectionTitle;
        Content = GuardNotEmpty(content, nameof(Content));
        ContentHash = GuardNotEmpty(contentHash, nameof(ContentHash));
        TokenCount = GuardPositive(tokenCount, nameof(TokenCount));
        CreatedAt = DateTimeOffset.UtcNow;
    }

    private DocumentChunk() { } // EF Core

    public static DocumentChunk Create(
        Guid id,
        Guid tenantId,
        Guid documentId,
        Guid versionId,
        int chunkIndex,
        string content,
        string contentHash,
        int tokenCount,
        int? pageNumber = null,
        string? sectionTitle = null)
    {
        return new DocumentChunk(
            id, tenantId, documentId, versionId,
            chunkIndex, pageNumber, sectionTitle,
            content, contentHash, tokenCount);
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

    private static int GuardNonNegative(int value, string paramName)
    {
        if (value < 0)
        {
            throw new DomainException($"{paramName} must be >= 0.");
        }

        return value;
    }

    private static int GuardPositive(int value, string paramName)
    {
        if (value <= 0)
        {
            throw new DomainException($"{paramName} must be > 0.");
        }

        return value;
    }
}
