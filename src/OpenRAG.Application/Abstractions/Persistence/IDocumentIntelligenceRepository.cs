using OpenRAG.Domain.Documents;

namespace OpenRAG.Application.Abstractions.Persistence;

/// <summary>
/// Repository for DocumentIntelligence records.
/// </summary>
public interface IDocumentIntelligenceRepository
{
    /// <summary>
    /// Returns the intelligence record for a version, or null.
    /// </summary>
    Task<DocumentIntelligence?> GetByVersionAsync(
        Guid tenantId,
        Guid documentId,
        Guid versionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a new intelligence record.
    /// </summary>
    Task AddAsync(
        DocumentIntelligence intelligence,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes all intelligence records for a version (used before re-generation).
    /// </summary>
    Task DeleteByVersionAsync(
        Guid tenantId,
        Guid documentId,
        Guid versionId,
        CancellationToken cancellationToken = default);
}
