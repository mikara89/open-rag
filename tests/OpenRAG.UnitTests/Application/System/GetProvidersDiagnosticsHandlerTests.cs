using Microsoft.Extensions.Configuration;
using OpenRAG.Application.System.GetProvidersDiagnostics;

namespace OpenRAG.UnitTests.Application.GetProvidersDiagnostics;

public sealed class GetProvidersDiagnosticsHandlerTests
{
    [Fact]
    public async Task Reports_provider_names()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Preprocessing:Docling:Provider"] = "DoclingServe",
                ["Preprocessing:Docling:BaseUrl"] = "http://localhost:5001",
                ["Preprocessing:Docling:ConvertFilePath"] = "/v1/convert/file",
                ["Chunking:Provider"] = "DoclingJson",
                ["AI:Embeddings:Provider"] = "Mock",
                ["AI:Chat:Provider"] = "Mock",
                ["Storage:Provider"] = "Local"
            })
            .Build();

        var handler = new GetProvidersDiagnosticsHandler(config);
        var response = await handler.Handle(new GetProvidersDiagnosticsQuery(), CancellationToken.None);

        Assert.Equal("DoclingServe", response.Preprocessing.Provider);
        Assert.Equal("DoclingJson", response.Chunking.Provider);
        Assert.Equal("Mock", response.Embeddings.Provider);
        Assert.Equal("Mock", response.Chat.Provider);
        Assert.Equal("Local", response.Storage.Provider);
    }

    [Fact]
    public async Task Reports_configured_as_true_when_valid()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Preprocessing:Docling:Provider"] = "Mock",
                ["Chunking:Provider"] = "DoclingJson",
                ["AI:Embeddings:Provider"] = "Mock",
                ["AI:Chat:Provider"] = "Mock",
                ["Storage:Provider"] = "Local"
            })
            .Build();

        var handler = new GetProvidersDiagnosticsHandler(config);
        var response = await handler.Handle(new GetProvidersDiagnosticsQuery(), CancellationToken.None);

        Assert.True(response.Preprocessing.Configured);
    }

    [Fact]
    public async Task Reports_configured_as_false_when_DoclingServe_missing_BaseUrl()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Preprocessing:Docling:Provider"] = "DoclingServe",
                ["Preprocessing:Docling:BaseUrl"] = "",
                ["Chunking:Provider"] = "DoclingJson",
                ["AI:Embeddings:Provider"] = "Mock",
                ["AI:Chat:Provider"] = "Mock",
                ["Storage:Provider"] = "Local"
            })
            .Build();

        var handler = new GetProvidersDiagnosticsHandler(config);
        var response = await handler.Handle(new GetProvidersDiagnosticsQuery(), CancellationToken.None);

        Assert.False(response.Preprocessing.Configured);
        Assert.NotNull(response.Preprocessing.ValidationErrors);
        Assert.Contains(response.Preprocessing.ValidationErrors,
            e => e.Contains("BaseUrl"));
    }

    [Fact]
    public async Task Does_not_expose_api_key_in_response()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Preprocessing:Docling:Provider"] = "Mock",
                ["Chunking:Provider"] = "DoclingJson",
                ["AI:Embeddings:Provider"] = "Mock",
                ["AI:Chat:Provider"] = "OpenAICompatible",
                ["AI:Chat:BaseUrl"] = "https://api.deepseek.com/v1",
                ["AI:Chat:Model"] = "deepseek-chat",
                ["AI:Chat:ApiKey"] = "sk-very-secret-key-do-not-expose",
                ["Storage:Provider"] = "Local"
            })
            .Build();

        var handler = new GetProvidersDiagnosticsHandler(config);
        var response = await handler.Handle(new GetProvidersDiagnosticsQuery(), CancellationToken.None);

        // Serialize the response and verify no secret key is exposed
        var json = global::System.Text.Json.JsonSerializer.Serialize(response);
        Assert.DoesNotContain("sk-very-secret-key-do-not-expose", json);
        // Verify that apiKeyPresent is the only key-related field (no raw key exposed)
        Assert.Contains("apiKeyPresent", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"apiKey\"", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Reports_api_key_present_when_configured()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Preprocessing:Docling:Provider"] = "Mock",
                ["Chunking:Provider"] = "DoclingJson",
                ["AI:Embeddings:Provider"] = "Mock",
                ["AI:Chat:Provider"] = "OpenAICompatible",
                ["AI:Chat:BaseUrl"] = "https://api.deepseek.com/v1",
                ["AI:Chat:Model"] = "deepseek-chat",
                ["AI:Chat:ApiKey"] = "sk-real-key",
                ["Storage:Provider"] = "Local"
            })
            .Build();

        var handler = new GetProvidersDiagnosticsHandler(config);
        var response = await handler.Handle(new GetProvidersDiagnosticsQuery(), CancellationToken.None);

        Assert.True(response.Chat.ApiKeyPresent);
    }

    [Fact]
    public async Task Reports_api_key_missing_when_placeholder()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Preprocessing:Docling:Provider"] = "Mock",
                ["Chunking:Provider"] = "DoclingJson",
                ["AI:Embeddings:Provider"] = "Mock",
                ["AI:Chat:Provider"] = "OpenAICompatible",
                ["AI:Chat:BaseUrl"] = "https://api.deepseek.com/v1",
                ["AI:Chat:Model"] = "deepseek-chat",
                ["AI:Chat:ApiKey"] = "YOUR_DEEPSEEK_API_KEY_HERE",
                ["Storage:Provider"] = "Local"
            })
            .Build();

        var handler = new GetProvidersDiagnosticsHandler(config);
        var response = await handler.Handle(new GetProvidersDiagnosticsQuery(), CancellationToken.None);

        Assert.False(response.Chat.ApiKeyPresent);
    }

    [Fact]
    public async Task Reports_base_url_and_model()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Preprocessing:Docling:Provider"] = "Mock",
                ["Chunking:Provider"] = "DoclingJson",
                ["AI:Embeddings:Provider"] = "OpenAICompatible",
                ["AI:Embeddings:BaseUrl"] = "http://localhost:1234/v1",
                ["AI:Embeddings:Model"] = "nomic-embed-text",
                ["AI:Embeddings:ApiKey"] = "lm-studio",
                ["AI:Chat:Provider"] = "OpenAICompatible",
                ["AI:Chat:BaseUrl"] = "https://api.deepseek.com/v1",
                ["AI:Chat:Model"] = "deepseek-chat",
                ["AI:Chat:ApiKey"] = "sk-test",
                ["Storage:Provider"] = "Local"
            })
            .Build();

        var handler = new GetProvidersDiagnosticsHandler(config);
        var response = await handler.Handle(new GetProvidersDiagnosticsQuery(), CancellationToken.None);

        Assert.Equal("http://localhost:1234/v1", response.Embeddings.BaseUrl);
        Assert.Equal("nomic-embed-text", response.Embeddings.Model);
        Assert.Equal("https://api.deepseek.com/v1", response.Chat.BaseUrl);
        Assert.Equal("deepseek-chat", response.Chat.Model);
    }

    [Fact]
    public async Task Reports_chunking_params()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Preprocessing:Docling:Provider"] = "Mock",
                ["Chunking:Provider"] = "DoclingJson",
                ["Chunking:MaxChunkCharacters"] = "1500",
                ["Chunking:OverlapCharacters"] = "100",
                ["AI:Embeddings:Provider"] = "Mock",
                ["AI:Chat:Provider"] = "Mock",
                ["Storage:Provider"] = "Local"
            })
            .Build();

        var handler = new GetProvidersDiagnosticsHandler(config);
        var response = await handler.Handle(new GetProvidersDiagnosticsQuery(), CancellationToken.None);

        Assert.Equal(1500, response.Chunking.MaxChunkCharacters);
        Assert.Equal(100, response.Chunking.OverlapCharacters);
    }

    [Fact]
    public async Task Reports_storage_provider_and_path()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Preprocessing:Docling:Provider"] = "Mock",
                ["Chunking:Provider"] = "DoclingJson",
                ["AI:Embeddings:Provider"] = "Mock",
                ["AI:Chat:Provider"] = "Mock",
                ["Storage:Provider"] = "Local",
                ["Storage:LocalRootPath"] = "/data/openrag-storage"
            })
            .Build();

        var handler = new GetProvidersDiagnosticsHandler(config);
        var response = await handler.Handle(new GetProvidersDiagnosticsQuery(), CancellationToken.None);

        Assert.Equal("Local", response.Storage.Provider);
        Assert.Equal("/data/openrag-storage", response.Storage.LocalRootPath);
    }

    [Fact]
    public async Task Defaults_providers_when_not_configured()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var handler = new GetProvidersDiagnosticsHandler(config);
        var response = await handler.Handle(new GetProvidersDiagnosticsQuery(), CancellationToken.None);

        Assert.Equal("Mock", response.Preprocessing.Provider);
        Assert.Equal("DoclingJson", response.Chunking.Provider);
        Assert.Equal("Mock", response.Embeddings.Provider);
        Assert.Equal("Mock", response.Chat.Provider);
        Assert.Equal("Local", response.Storage.Provider);
    }
}
