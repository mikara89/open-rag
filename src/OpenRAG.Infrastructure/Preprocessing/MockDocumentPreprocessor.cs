using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenRAG.Application.Abstractions.Processing;
using OpenRAG.Application.Abstractions.Storage;

namespace OpenRAG.Infrastructure.Preprocessing;

/// <summary>
/// Mock document preprocessor for local development.
/// Reads the original file, produces simple Markdown/JSON artifacts,
/// and saves them through IFileStorage.
/// TODO: Replace with DoclingServePreprocessor when Docling Serve is added to Aspire.
/// </summary>
public sealed class MockDocumentPreprocessor : IDocumentPreprocessor
{
    private readonly IFileStorage _fileStorage;
    private readonly IDocumentObjectKeyPolicy _objectKeyPolicy;

    public MockDocumentPreprocessor(
        IFileStorage fileStorage,
        IDocumentObjectKeyPolicy objectKeyPolicy)
    {
        _fileStorage = fileStorage;
        _objectKeyPolicy = objectKeyPolicy;
    }

    public async Task<DocumentPreprocessingResult> PreprocessAsync(
        DocumentPreprocessingRequest request,
        CancellationToken cancellationToken = default)
    {
        _objectKeyPolicy.EnsureOwned(
            request.OriginalObjectKey,
            request.TenantId,
            request.DocumentId,
            request.VersionId,
            DocumentObjectKind.Source);

        var markdownKey = _objectKeyPolicy.BuildArtifactKey(
            request.TenantId,
            request.DocumentId,
            request.VersionId,
            DocumentObjectKind.Markdown);
        var jsonKey = _objectKeyPolicy.BuildArtifactKey(
            request.TenantId,
            request.DocumentId,
            request.VersionId,
            DocumentObjectKind.Json);

        // Produce mock Markdown
        var markdownContent = $"# {request.FileName}\n\nMock preprocessed content for {request.FileName}.\n";
        var markdownBytes = Encoding.UTF8.GetBytes(markdownContent);
        using var markdownStream = new MemoryStream(markdownBytes);

        // Save Markdown artifact
        await _fileStorage.SaveAsync(
            markdownStream, markdownKey, "text/markdown", cancellationToken);

        // Produce mock JSON
        var jsonContent = JsonSerializer.Serialize(new
        {
            source = "mock",
            fileName = request.FileName,
            mimeType = request.MimeType,
            documentId = request.DocumentId.ToString(),
            versionId = request.VersionId.ToString()
        });
        var jsonBytes = Encoding.UTF8.GetBytes(jsonContent);
        using var jsonStream = new MemoryStream(jsonBytes);

        // Save JSON artifact
        await _fileStorage.SaveAsync(
            jsonStream, jsonKey, "application/json", cancellationToken);

        // Compute hashes
        var markdownSha256 = ComputeSha256(markdownBytes);
        var jsonSha256 = ComputeSha256(jsonBytes);

        return new DocumentPreprocessingResult(
            MarkdownObjectKey: markdownKey,
            JsonObjectKey: jsonKey,
            MarkdownSha256: markdownSha256,
            JsonSha256: jsonSha256);
    }

    private static string ComputeSha256(byte[] content)
    {
        var hash = SHA256.HashData(content);
        return Convert.ToHexStringLower(hash);
    }
}
