using OpenRAG.Domain.Common;

namespace OpenRAG.Domain.Documents;

/// <summary>
/// Stores generated intelligence metadata for a document version:
/// classification, summary, keywords, entities, and extracted metadata.
/// One active record per version; reprocessing replaces it.
/// </summary>
public sealed class DocumentIntelligence : Entity
{
    public Guid TenantId { get; private set; }
    public Guid DocumentId { get; private set; }
    public Guid VersionId { get; private set; }
    public string? Classification { get; private set; }
    public string? Summary { get; private set; }
    public string? KeywordsJson { get; private set; }
    public string? EntitiesJson { get; private set; }
    public string? ExtractedMetadataJson { get; private set; }
    public string Provider { get; private set; }
    public string Model { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private DocumentIntelligence(
        Guid id,
        Guid tenantId,
        Guid documentId,
        Guid versionId,
        string? classification,
        string? summary,
        string? keywordsJson,
        string? entitiesJson,
        string? extractedMetadataJson,
        string provider,
        string model)
        : base(id)
    {
        TenantId = GuardNotEmpty(tenantId, nameof(TenantId));
        DocumentId = GuardNotEmpty(documentId, nameof(DocumentId));
        VersionId = GuardNotEmpty(versionId, nameof(VersionId));
        Classification = classification;
        Summary = summary;
        KeywordsJson = keywordsJson;
        EntitiesJson = entitiesJson;
        ExtractedMetadataJson = extractedMetadataJson;
        Provider = GuardNotEmpty(provider, nameof(Provider));
        Model = GuardNotEmpty(model, nameof(Model));
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    private DocumentIntelligence() { } // EF Core

    public static DocumentIntelligence Create(
        Guid id,
        Guid tenantId,
        Guid documentId,
        Guid versionId,
        string? classification,
        string? summary,
        string? keywordsJson,
        string? entitiesJson,
        string? extractedMetadataJson,
        string provider,
        string model)
    {
        return new DocumentIntelligence(
            id, tenantId, documentId, versionId,
            classification, summary, keywordsJson, entitiesJson,
            extractedMetadataJson, provider, model);
    }

    public void Update(
        string? classification,
        string? summary,
        string? keywordsJson,
        string? entitiesJson,
        string? extractedMetadataJson,
        string provider,
        string model)
    {
        Classification = classification;
        Summary = summary;
        KeywordsJson = keywordsJson;
        EntitiesJson = entitiesJson;
        ExtractedMetadataJson = extractedMetadataJson;
        Provider = GuardNotEmpty(provider, nameof(Provider));
        Model = GuardNotEmpty(model, nameof(Model));
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static Guid GuardNotEmpty(Guid value, string paramName)
    {
        if (value == Guid.Empty)
            throw new DomainException($"{paramName} cannot be empty.");
        return value;
    }

    private static string GuardNotEmpty(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new DomainException($"{paramName} cannot be empty.");
        return value;
    }
}
