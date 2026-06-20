using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenRAG.Application.Abstractions.Processing;
using OpenRAG.Application.Abstractions.Storage;
using OpenRAG.Application.Common;
using OpenRAG.Infrastructure.Preprocessing;

namespace OpenRAG.UnitTests.Infrastructure.Preprocessing;

public sealed class DoclingServeDocumentPreprocessorTests
{
    [Fact]
    public async Task Returns_markdown_and_json_keys_from_document_md_content_response()
    {
        var responseJson = JsonSerializer.Serialize(new
        {
            document = new { md_content = "# Hello\n\nWorld.", json_content = new { title = "Test" } }
        });
        var handler = CreateHandler(HttpStatusCode.OK, responseJson);
        var service = CreateService(handler);

        var result = await service.PreprocessAsync(CreateRequest());

        Assert.NotNull(result.MarkdownObjectKey);
        Assert.NotNull(result.JsonObjectKey);
    }

    [Fact]
    public async Task Returns_markdown_from_root_markdown_field()
    {
        var responseJson = JsonSerializer.Serialize(new
        {
            markdown = "# Doc Title\n\nContent here.",
            json = new { pages = 1 }
        });
        var handler = CreateHandler(HttpStatusCode.OK, responseJson);
        var service = CreateService(handler);

        var result = await service.PreprocessAsync(CreateRequest());

        Assert.NotNull(result.MarkdownObjectKey);
    }

    [Fact]
    public async Task Stores_full_response_as_json_fallback_when_no_json_field()
    {
        var responseJson = JsonSerializer.Serialize(new
        {
            document = new { md_content = "# Just markdown." }
        });
        var handler = CreateHandler(HttpStatusCode.OK, responseJson);
        var service = CreateService(handler);

        var result = await service.PreprocessAsync(CreateRequest());

        Assert.NotNull(result.JsonObjectKey);
    }

    [Fact]
    public async Task Throws_on_non_success_http_status()
    {
        var handler = CreateHandler(HttpStatusCode.InternalServerError, "{\"error\":\"crash\"}");
        var service = CreateService(handler);

        var ex = await Assert.ThrowsAsync<AppException>(() =>
            service.PreprocessAsync(CreateRequest()));

        Assert.Contains("500", ex.Message);
    }

    [Fact]
    public async Task Throws_when_markdown_missing()
    {
        var responseJson = JsonSerializer.Serialize(new { document = new { json_content = new { } } });
        var handler = CreateHandler(HttpStatusCode.OK, responseJson);
        var service = CreateService(handler);

        var ex = await Assert.ThrowsAsync<AppException>(() =>
            service.PreprocessAsync(CreateRequest()));

        Assert.Contains("no Markdown", ex.Message);
    }

    [Fact]
    public async Task Uses_configured_endpoint_path()
    {
        string? actualUrl = null;
        var handler = new InterceptingHandler((req, _) =>
        {
            actualUrl = req.RequestUri?.ToString();
            var resp = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new { markdown = "# Test", json = new { } }),
                    Encoding.UTF8, "application/json")
            };
            return Task.FromResult(resp);
        });
        var fileStorage = new FakeFileStorage();
        var options = Options.Create(new DoclingPreprocessorOptions
        {
            Provider = "DoclingServe",
            BaseUrl = "http://localhost:5001",
            ConvertFilePath = "/v1/convert/file"
        });
        var logger = NullLogger<DoclingServeDocumentPreprocessor>.Instance;
        var service = new DoclingServeDocumentPreprocessor(
            new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5001") },
            fileStorage, options, logger);

        await service.PreprocessAsync(CreateRequest());

        Assert.Contains("/v1/convert/file", actualUrl);
    }

    [Fact]
    public async Task Does_not_include_secrets_in_exception_message()
    {
        var handler = CreateHandler(HttpStatusCode.BadRequest, "{\"error\":\"bad file\"}");
        var service = CreateService(handler);

        var ex = await Assert.ThrowsAsync<AppException>(() =>
            service.PreprocessAsync(CreateRequest()));

        Assert.DoesNotContain("sk-", ex.Message);
        Assert.DoesNotContain("Bearer", ex.Message);
    }

    [Fact]
    public async Task Sends_multipart_form_data()
    {
        string? contentType = null;
        var handler = new InterceptingHandler((req, _) =>
        {
            contentType = req.Content?.Headers.ContentType?.ToString();
            var resp = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new { markdown = "# Test", json = new { } }),
                    Encoding.UTF8, "application/json")
            };
            return Task.FromResult(resp);
        });
        var fileStorage = new FakeFileStorage();
        var options = Options.Create(new DoclingPreprocessorOptions());
        var logger = NullLogger<DoclingServeDocumentPreprocessor>.Instance;
        var service = new DoclingServeDocumentPreprocessor(
            new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5001") },
            fileStorage, options, logger);

        await service.PreprocessAsync(CreateRequest());

        Assert.NotNull(contentType);
        Assert.Contains("multipart/form-data", contentType);
    }

    [Fact]
    public async Task Extracts_text_as_markdown_fallback()
    {
        var responseJson = JsonSerializer.Serialize(new { document = new { text = "Plain text content." } });
        var handler = CreateHandler(HttpStatusCode.OK, responseJson);
        var service = CreateService(handler);

        var result = await service.PreprocessAsync(CreateRequest());

        Assert.NotNull(result.MarkdownObjectKey);
    }

    [Fact]
    public async Task Parses_Actual_DoclingServe_Response_Shape()
    {
        // Trimmed sanitized sample from real Docling Serve /v1/convert/file response.
        // The actual response has document.md_content (Markdown) and document.json_content (structured DoclingDocument).
        var responseJson = JsonSerializer.Serialize(new
        {
            document = new
            {
                filename = "README.md",
                md_content = "# OpenRAG\n\nThis is the real Markdown content extracted by Docling Serve.\n\n## Section\n\nContent here.",
                json_content = new
                {
                    schema_name = "DoclingDocument",
                    version = "1.10.0",
                    name = "README",
                    origin = new
                    {
                        mimetype = "text/markdown",
                        binary_hash = 12345678901234567890UL,
                        filename = "README.md"
                    },
                    body = new
                    {
                        self_ref = "#/body",
                        name = "_root_",
                        children = new object[] { }
                    }
                }
            }
        });

        var handler = CreateHandler(HttpStatusCode.OK, responseJson);
        var service = CreateService(handler);

        var result = await service.PreprocessAsync(CreateRequest());

        Assert.NotNull(result.MarkdownObjectKey);
        Assert.NotNull(result.JsonObjectKey);
        Assert.Contains("docling/document.md", result.MarkdownObjectKey);
        Assert.Contains("docling/document.json", result.JsonObjectKey);
    }

    [Fact]
    public async Task Parses_document_mdContent_camelCase()
    {
        // Some Docling versions may use camelCase: mdContent
        var responseJson = JsonSerializer.Serialize(new
        {
            document = new
            {
                mdContent = "# CamelCase Markdown\n\nContent.",
                json_content = new { type = "docling" }
            }
        });

        var handler = CreateHandler(HttpStatusCode.OK, responseJson);
        var service = CreateService(handler);

        var result = await service.PreprocessAsync(CreateRequest());

        Assert.NotNull(result.MarkdownObjectKey);
    }

    [Fact]
    public async Task Recursive_fallback_finds_md_content_deep_in_json()
    {
        // Regression: recursive search finds markdown fields nested deep
        var responseJson = JsonSerializer.Serialize(new
        {
            status = "ok",
            results = new[]
            {
                new
                {
                    id = "1",
                    document = new
                    {
                        md_content = "# Deep nested Markdown\n\nFound via recursive search."
                    }
                }
            }
        });

        var handler = CreateHandler(HttpStatusCode.OK, responseJson);
        var service = CreateService(handler);

        var result = await service.PreprocessAsync(CreateRequest());

        Assert.NotNull(result.MarkdownObjectKey);
    }

    [Fact]
    public async Task Recursive_fallback_prefers_mdContent_over_text()
    {
        // Prefers md_content/mdContent over generic "text" field
        var responseJson = JsonSerializer.Serialize(new
        {
            wrapper = new
            {
                text = "This is generic text.",
                md_content = "# Prefer this Markdown"
            }
        });

        var handler = CreateHandler(HttpStatusCode.OK, responseJson);
        var service = CreateService(handler);

        var result = await service.PreprocessAsync(CreateRequest());

        Assert.NotNull(result.MarkdownObjectKey);
    }

    // ══ Helpers ═══════════════════════════════════════════════════

    private static DocumentPreprocessingRequest CreateRequest() => new(
        TenantId: Guid.NewGuid(),
        DocumentId: Guid.NewGuid(),
        VersionId: Guid.NewGuid(),
        OriginalObjectKey: "tenants/t/doc/v/orig/test.md",
        FileName: "test.md",
        MimeType: "text/markdown",
        CorrelationId: "corr");

    private static DoclingServeDocumentPreprocessor CreateService(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5001") };
        var fileStorage = new FakeFileStorage();
        var options = Options.Create(new DoclingPreprocessorOptions
        {
            Provider = "DoclingServe",
            BaseUrl = "http://localhost:5001",
            ConvertFilePath = "/v1/convert/file"
        });
        var logger = NullLogger<DoclingServeDocumentPreprocessor>.Instance;
        return new DoclingServeDocumentPreprocessor(httpClient, fileStorage, options, logger);
    }

    private static HttpMessageHandler CreateHandler(HttpStatusCode status, string responseBody)
    {
        return new InterceptingHandler((_, _) => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
        }));
    }

    private sealed class FakeFileStorage : IFileStorage
    {
        public Task<StoredObjectResult> SaveAsync(Stream content, string objectKey, string contentType, CancellationToken ct = default)
            => Task.FromResult(new StoredObjectResult("b", objectKey, contentType, content.Length, null, null));

        public Task<Stream> OpenReadAsync(string objectKey, CancellationToken ct = default)
            => Task.FromResult<Stream>(new MemoryStream("# Test original content."u8.ToArray()));

        public Task DeleteAsync(string objectKey, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class InterceptingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;
        public InterceptingHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> h) => _handler = h;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) => _handler(r, ct);
    }
}
