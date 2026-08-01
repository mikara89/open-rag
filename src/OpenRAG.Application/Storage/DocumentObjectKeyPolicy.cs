using OpenRAG.Application.Abstractions.Storage;
using OpenRAG.Application.Common;

namespace OpenRAG.Application.Storage;

public sealed class DocumentObjectKeyPolicy : IDocumentObjectKeyPolicy
{
    public string BuildSourceKey(
        Guid tenantId,
        Guid documentId,
        Guid versionId,
        string fileName)
    {
        ValidateIds(tenantId, documentId, versionId);

        var safeFileName = Path.GetFileName(fileName?.Replace('\\', '/'));
        if (string.IsNullOrWhiteSpace(safeFileName)
            || safeFileName is "." or ".."
            || !string.Equals(safeFileName, fileName, StringComparison.Ordinal))
        {
            throw new RequestValidationException("The source file name is invalid.");
        }

        return $"{BuildPrefix(tenantId, documentId, versionId)}original/{safeFileName}";
    }

    public string BuildArtifactKey(
        Guid tenantId,
        Guid documentId,
        Guid versionId,
        DocumentObjectKind kind)
    {
        ValidateIds(tenantId, documentId, versionId);

        return kind switch
        {
            DocumentObjectKind.Markdown =>
                $"{BuildPrefix(tenantId, documentId, versionId)}docling/document.md",
            DocumentObjectKind.Json =>
                $"{BuildPrefix(tenantId, documentId, versionId)}docling/document.json",
            _ => throw new RequestValidationException("An artifact object kind is required.")
        };
    }

    public void EnsureOwned(
        string objectKey,
        Guid tenantId,
        Guid documentId,
        Guid versionId,
        DocumentObjectKind kind)
    {
        ValidateIds(tenantId, documentId, versionId);

        if (string.IsNullOrWhiteSpace(objectKey)
            || Path.IsPathFullyQualified(objectKey)
            || objectKey.StartsWith('/')
            || objectKey.Contains('\\'))
        {
            throw Violation();
        }

        var segments = objectKey.Split('/', StringSplitOptions.None);
        if (segments.Any(segment => string.IsNullOrWhiteSpace(segment) || segment is "." or ".."))
        {
            throw Violation();
        }

        var prefix = BuildPrefix(tenantId, documentId, versionId);
        if (!objectKey.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw Violation();
        }

        var suffix = objectKey[prefix.Length..];
        var isExpected = kind switch
        {
            DocumentObjectKind.Source =>
                suffix.StartsWith("original/", StringComparison.Ordinal)
                && suffix.Length > "original/".Length
                && !suffix["original/".Length..].Contains('/'),
            DocumentObjectKind.Markdown =>
                string.Equals(suffix, "docling/document.md", StringComparison.Ordinal),
            DocumentObjectKind.Json =>
                string.Equals(suffix, "docling/document.json", StringComparison.Ordinal),
            _ => false
        };

        if (!isExpected)
        {
            throw Violation();
        }
    }

    private static string BuildPrefix(Guid tenantId, Guid documentId, Guid versionId) =>
        $"tenants/{tenantId:D}/documents/{documentId:D}/versions/{versionId:D}/";

    private static void ValidateIds(Guid tenantId, Guid documentId, Guid versionId)
    {
        if (tenantId == Guid.Empty || documentId == Guid.Empty || versionId == Guid.Empty)
        {
            throw new RequestValidationException(
                "Tenant, document, and version identifiers must be non-empty.");
        }
    }

    private static IsolationViolationException Violation() =>
        new("A persisted document object key is outside its trusted ownership boundary.");
}
