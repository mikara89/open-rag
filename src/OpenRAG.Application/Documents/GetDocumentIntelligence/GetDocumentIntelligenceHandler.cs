using System.Text.Json;
using Mediator;
using OpenRAG.Application.Abstractions.AI;
using OpenRAG.Application.Abstractions.Persistence;
using OpenRAG.Application.Abstractions.Security;
using OpenRAG.Application.Common;

namespace OpenRAG.Application.Documents.GetDocumentIntelligence;

public sealed class GetDocumentIntelligenceHandler
    : IRequestHandler<GetDocumentIntelligenceQuery, DocumentIntelligenceResponse?>
{
    private readonly IDocumentRepository _documentRepository;
    private readonly IDocumentIntelligenceRepository _intelligenceRepository;
    private readonly ICurrentTenant _currentTenant;

    public GetDocumentIntelligenceHandler(
        IDocumentRepository documentRepository,
        IDocumentIntelligenceRepository intelligenceRepository,
        ICurrentTenant currentTenant)
    {
        _documentRepository = documentRepository;
        _intelligenceRepository = intelligenceRepository;
        _currentTenant = currentTenant;
    }

    public async ValueTask<DocumentIntelligenceResponse?> Handle(
        GetDocumentIntelligenceQuery query,
        CancellationToken cancellationToken = default)
    {
        if (query.DocumentId == Guid.Empty)
            throw new AppException("DocumentId cannot be empty.");

        if (query.VersionId == Guid.Empty)
            throw new AppException("VersionId cannot be empty.");

        var tenantId = _currentTenant.TenantId;

        // Verify document exists and belongs to tenant
        var document = await _documentRepository.GetByIdWithVersionsAsync(
            tenantId, query.DocumentId, cancellationToken);

        if (document is null)
            throw new AppException($"Document '{query.DocumentId}' not found.");

        var intel = await _intelligenceRepository.GetByVersionAsync(
            tenantId, query.DocumentId, query.VersionId, cancellationToken);

        if (intel is null)
            return null;

        return new DocumentIntelligenceResponse(
            Classification: intel.Classification,
            Summary: intel.Summary,
            Keywords: DeserializeList(intel.KeywordsJson),
            Entities: DeserializeEntities(intel.EntitiesJson),
            Metadata: DeserializeDictionary(intel.ExtractedMetadataJson),
            Provider: intel.Provider,
            Model: intel.Model,
            CreatedAt: intel.CreatedAt,
            UpdatedAt: intel.UpdatedAt);
    }

    private static IReadOnlyList<string> DeserializeList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<string>();

        try
        {
            var result = JsonSerializer.Deserialize<List<string>>(json);
            return result?.AsReadOnly() ?? (IReadOnlyList<string>)Array.Empty<string>();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static IReadOnlyList<IntelligenceEntity> DeserializeEntities(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<IntelligenceEntity>();

        try
        {
            var result = JsonSerializer.Deserialize<List<IntelligenceEntity>>(json);
            return result?.AsReadOnly() ?? (IReadOnlyList<IntelligenceEntity>)Array.Empty<IntelligenceEntity>();
        }
        catch
        {
            return Array.Empty<IntelligenceEntity>();
        }
    }

    private static IReadOnlyDictionary<string, string> DeserializeDictionary(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new Dictionary<string, string>();

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                   ?? new Dictionary<string, string>();
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }
}

public sealed record DocumentIntelligenceResponse(
    string? Classification,
    string? Summary,
    IReadOnlyList<string> Keywords,
    IReadOnlyList<IntelligenceEntity> Entities,
    IReadOnlyDictionary<string, string> Metadata,
    string Provider,
    string Model,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);
