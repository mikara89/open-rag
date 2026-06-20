using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using OpenRAG.Application.Abstractions.Processing;
using OpenRAG.Application.Abstractions.Storage;
using OpenRAG.Application.Common;

namespace OpenRAG.Infrastructure.Preprocessing;

/// <summary>
/// Docling Serve REST preprocessor.
/// Calls Docling Serve /v1/convert/file endpoint to extract Markdown and JSON from documents.
/// </summary>
public sealed class DoclingServeDocumentPreprocessor : IDocumentPreprocessor
{
    private readonly HttpClient _httpClient;
    private readonly IFileStorage _fileStorage;
    private readonly DoclingPreprocessorOptions _options;
    private readonly ILogger<DoclingServeDocumentPreprocessor> _logger;

    private static readonly JsonSerializerOptions s_indentedOptions = new() { WriteIndented = true };

    public DoclingServeDocumentPreprocessor(
        HttpClient httpClient,
        IFileStorage fileStorage,
        Microsoft.Extensions.Options.IOptions<DoclingPreprocessorOptions> options,
        ILogger<DoclingServeDocumentPreprocessor> logger)
    {
        _httpClient = httpClient;
        _fileStorage = fileStorage;
        _options = options.Value;
        _logger = logger;

        if (string.IsNullOrWhiteSpace(_options.BaseUrl))
            throw new InvalidOperationException("Docling BaseUrl cannot be empty.");
    }

    public async Task<DocumentPreprocessingResult> PreprocessAsync(
        DocumentPreprocessingRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.OriginalObjectKey))
            throw new AppException("OriginalObjectKey cannot be empty.");

        // 1. Read original file from IFileStorage
        await using var originalStream = await _fileStorage.OpenReadAsync(
            request.OriginalObjectKey, cancellationToken);

        // Determine filename for multipart form
        var fileName = request.OriginalObjectKey.Split('/').Last();

        // 2. Build multipart/form-data request
        var url = $"{_options.BaseUrl.TrimEnd('/')}{_options.ConvertFilePath}";

        using var content = new MultipartFormDataContent();
        var streamContent = new StreamContent(originalStream);
        streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
            request.MimeType ?? "application/octet-stream");
        content.Add(streamContent, "files", fileName);

        // Request Markdown and JSON output formats
        content.Add(new StringContent("md"), "to_formats");
        content.Add(new StringContent("json"), "to_formats");

        if (_options.EnableOcr)
            content.Add(new StringContent("true"), "do_ocr");

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsync(url, content, cancellationToken);
        }
        catch (Exception ex) when (ex is not AppException)
        {
            _logger.LogError(ex, "Failed to call Docling Serve at {Url}", url);
            throw new AppException(
                $"Failed to call Docling Serve at {url}: {ex.Message}", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            var truncatedBody = errorBody.Length > 500 ? errorBody[..500] + "..." : errorBody;
            _logger.LogError(
                "Docling Serve returned HTTP {StatusCode} from {Url}: {Body}",
                (int)response.StatusCode, url, truncatedBody);
            throw new AppException(
                $"Docling Serve returned HTTP {(int)response.StatusCode} from {url}: {truncatedBody}");
        }

        // 3. Parse response (tolerant extraction)
        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        var markdown = ExtractMarkdown(responseJson);
        var doclingJson = ExtractJsonArtifact(responseJson);

        if (string.IsNullOrWhiteSpace(markdown))
        {
            var preview = responseJson.Length > 2000 ? responseJson[..2000] + "..." : responseJson;
            _logger.LogError(
                "Docling Serve returned HTTP 200 but no Markdown content was found. " +
                "Endpoint={Url}, StatusCode=200, ResponsePreview={Preview}",
                url, preview);
            throw new AppException(
                $"Docling Serve returned a response but no Markdown content was found. " +
                $"Endpoint: {url}. Response preview: {preview}");
        }

        // 4. Store artifacts through IFileStorage
        var basePath = $"tenants/{request.TenantId}/documents/{request.DocumentId}/versions/{request.VersionId}/docling";
        var markdownKey = $"{basePath}/document.md";
        var jsonKey = $"{basePath}/document.json";

        var markdownBytes = Encoding.UTF8.GetBytes(markdown);
        using var mdStream = new MemoryStream(markdownBytes);
        await _fileStorage.SaveAsync(mdStream, markdownKey, "text/markdown", cancellationToken);

        var jsonBytes = Encoding.UTF8.GetBytes(doclingJson);
        using var jsonStream = new MemoryStream(jsonBytes);
        await _fileStorage.SaveAsync(jsonStream, jsonKey, "application/json", cancellationToken);

        var markdownSha256 = ComputeSha256(markdownBytes);
        var jsonSha256 = ComputeSha256(jsonBytes);

        _logger.LogInformation(
            "Docling preprocessing completed: DocumentId={DocumentId}, MarkdownKey={MarkdownKey}, JsonKey={JsonKey}",
            request.DocumentId, markdownKey, jsonKey);

        return new DocumentPreprocessingResult(
            MarkdownObjectKey: markdownKey,
            JsonObjectKey: jsonKey,
            MarkdownSha256: markdownSha256,
            JsonSha256: jsonSha256);
    }

    // ── Tolerant Markdown extraction ──────────────────────────────

    private static string? ExtractMarkdown(string responseJson)
    {
        using var doc = JsonDocument.Parse(responseJson);
        var root = doc.RootElement;

        // ── 1. Try known exact paths ──────────────────────────

        // Try nested: document.md_content, document.markdown, document.text
        if (root.TryGetProperty("document", out var docEl))
        {
            if (TryGetString(docEl, "md_content", out var md)) return md;
            if (TryGetString(docEl, "mdContent", out md)) return md;
            if (TryGetString(docEl, "markdown", out md)) return md;
            if (TryGetString(docEl, "text", out var txt)) return txt;

            // document.content.md, document.exported.md, document.outputs.md
            if (docEl.TryGetProperty("content", out var contentEl) &&
                TryGetString(contentEl, "md", out md)) return md;
            if (docEl.TryGetProperty("exported", out var exportedEl) &&
                TryGetString(exportedEl, "md", out md)) return md;
            if (docEl.TryGetProperty("outputs", out var outputsEl) &&
                TryGetString(outputsEl, "md", out md)) return md;
        }

        // Try root-level fields
        if (TryGetString(root, "md_content", out var md2)) return md2;
        if (TryGetString(root, "mdContent", out md2)) return md2;
        if (TryGetString(root, "markdown", out md2)) return md2;
        if (TryGetString(root, "text", out var txt2)) return txt2;

        // Try documents[0].md_content
        if (root.TryGetProperty("documents", out var docsEl) &&
            docsEl.ValueKind == JsonValueKind.Array &&
            docsEl.GetArrayLength() > 0)
        {
            var firstDoc = docsEl[0];
            if (TryGetString(firstDoc, "md_content", out var md3)) return md3;
        }

        // Try results[0].document.md_content, results[0].md_content
        if (root.TryGetProperty("results", out var resultsEl) &&
            resultsEl.ValueKind == JsonValueKind.Array &&
            resultsEl.GetArrayLength() > 0)
        {
            var firstResult = resultsEl[0];
            if (firstResult.TryGetProperty("document", out var resultDocEl) &&
                TryGetString(resultDocEl, "md_content", out var md4)) return md4;
            if (TryGetString(firstResult, "md_content", out var md5)) return md5;
        }

        // ── 2. Tolerant recursive search as fallback ──────────
        var (found, value) = RecursiveFindMarkdown(root);
        if (found && !string.IsNullOrWhiteSpace(value))
            return value;

        return null;
    }

    /// <summary>
    /// Recursively searches for a string property named md_content, mdContent, markdown, or text.
    /// Prefers md_content/mdContent over markdown over text.
    /// Returns (found, value).
    /// </summary>
    private static (bool Found, string? Value) RecursiveFindMarkdown(JsonElement element, int depth = 0)
    {
        const int maxDepth = 20;
        if (depth > maxDepth) return (false, null);

        string? textFallback = null;

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in element.EnumerateObject())
            {
                // Check exact field names
                if (prop.Value.ValueKind == JsonValueKind.String)
                {
                    var name = prop.Name;
                    if (name == "md_content" || name == "mdContent")
                    {
                        var val = prop.Value.GetString();
                        if (!string.IsNullOrWhiteSpace(val))
                            return (true, val);
                    }
                    if (name == "markdown")
                    {
                        var val = prop.Value.GetString();
                        if (!string.IsNullOrWhiteSpace(val))
                            return (true, val);
                    }
                    if (name == "text" && textFallback is null)
                    {
                        var val = prop.Value.GetString();
                        if (!string.IsNullOrWhiteSpace(val))
                            textFallback = val;
                    }
                }

                // Recurse into nested objects/arrays
                if (prop.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                {
                    var (found, val) = RecursiveFindMarkdown(prop.Value, depth + 1);
                    if (found) return (true, val);
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var (found, val) = RecursiveFindMarkdown(item, depth + 1);
                if (found) return (true, val);
            }
        }

        if (textFallback is not null)
            return (true, textFallback);

        return (false, null);
    }

    private static string ExtractJsonArtifact(string responseJson)
    {
        using var doc = JsonDocument.Parse(responseJson);
        var root = doc.RootElement;

        // Try to extract structured JSON document content
        if (root.TryGetProperty("document", out var docEl))
        {
            if (TryGetJsonValue(docEl, "json_content", out var jc)) return FormatJson(jc);
            if (TryGetJsonValue(docEl, "json", out var j)) return FormatJson(j);
            if (TryGetJsonValue(docEl, "content", out var c)) return FormatJson(c);
        }
        if (TryGetJsonValue(root, "json_content", out var jc2)) return FormatJson(jc2);
        if (TryGetJsonValue(root, "json", out var j2)) return FormatJson(j2);

        // Fallback: return full response as formatted JSON artifact (always written on HTTP 200)
        return FormatJson(responseJson);
    }

    private static bool TryGetString(JsonElement element, string propertyName, out string? value)
    {
        if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            value = prop.GetString();
            return !string.IsNullOrWhiteSpace(value);
        }
        value = null;
        return false;
    }

    private static bool TryGetJsonValue(JsonElement element, string propertyName, out string rawJson)
    {
        if (element.TryGetProperty(propertyName, out var prop) &&
            prop.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
        {
            rawJson = prop.GetRawText();
            return !string.IsNullOrWhiteSpace(rawJson) && rawJson != "{}" && rawJson != "[]";
        }
        rawJson = string.Empty;
        return false;
    }

    private static string FormatJson(string rawJson)
    {
        try
        {
            using var parsed = JsonDocument.Parse(rawJson);
            return JsonSerializer.Serialize(parsed, s_indentedOptions);
        }
        catch
        {
            return rawJson;
        }
    }

    private static string FormatJson(JsonElement element)
    {
        try
        {
            return JsonSerializer.Serialize(element, s_indentedOptions);
        }
        catch
        {
            return element.GetRawText();
        }
    }

    private static string ComputeSha256(byte[] content)
    {
        var hash = SHA256.HashData(content);
        return Convert.ToHexStringLower(hash);
    }
}
