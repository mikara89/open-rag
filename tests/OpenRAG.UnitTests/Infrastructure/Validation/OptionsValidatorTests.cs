using Microsoft.Extensions.Options;
using OpenRAG.Infrastructure.AI;
using OpenRAG.Infrastructure.Preprocessing;
using OpenRAG.Infrastructure.Processing;
using OpenRAG.Infrastructure.Storage;

namespace OpenRAG.UnitTests.Infrastructure.Validation;

public sealed class OptionsValidatorTests
{
    // ── Preprocessing ─────────────────────────────────────────────

    [Fact]
    public void Mock_preprocessing_passes_without_external_config()
    {
        var validator = new DoclingPreprocessorOptionsValidator();
        var options = new DoclingPreprocessorOptions { Provider = "Mock" };

        var result = validator.Validate(null, options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Unknown_preprocessing_provider_fails_clearly()
    {
        var validator = new DoclingPreprocessorOptionsValidator();
        var options = new DoclingPreprocessorOptions { Provider = "UnknownProvider" };

        var result = validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("not recognized", result.FailureMessage);
        Assert.Contains("UnknownProvider", result.FailureMessage);
    }

    [Fact]
    public void DoclingServe_missing_BaseUrl_fails_clearly()
    {
        var validator = new DoclingPreprocessorOptionsValidator();
        var options = new DoclingPreprocessorOptions
        {
            Provider = "DoclingServe",
            BaseUrl = "",
            ConvertFilePath = "/v1/convert/file"
        };

        var result = validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("BaseUrl", result.FailureMessage);
    }

    [Fact]
    public void DoclingServe_missing_ConvertFilePath_fails_clearly()
    {
        var validator = new DoclingPreprocessorOptionsValidator();
        var options = new DoclingPreprocessorOptions
        {
            Provider = "DoclingServe",
            BaseUrl = "http://localhost:5001",
            ConvertFilePath = ""
        };

        var result = validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("ConvertFilePath", result.FailureMessage);
    }

    [Fact]
    public void DoclingServe_with_valid_config_passes()
    {
        var validator = new DoclingPreprocessorOptionsValidator();
        var options = new DoclingPreprocessorOptions
        {
            Provider = "DoclingServe",
            BaseUrl = "http://localhost:5001",
            ConvertFilePath = "/v1/convert/file"
        };

        var result = validator.Validate(null, options);

        Assert.True(result.Succeeded);
    }

    // ── Chunking ──────────────────────────────────────────────────

    [Fact]
    public void Unknown_chunking_provider_fails_clearly()
    {
        var validator = new ChunkingOptionsValidator();
        var options = new ChunkingOptions { Provider = "UnknownChunker" };

        var result = validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("not recognized", result.FailureMessage);
        Assert.Contains("UnknownChunker", result.FailureMessage);
    }

    [Fact]
    public void DoclingJson_chunking_passes_without_external_config()
    {
        var validator = new ChunkingOptionsValidator();
        var options = new ChunkingOptions { Provider = "DoclingJson", MaxChunkCharacters = 2000 };

        var result = validator.Validate(null, options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Chunking_with_zero_max_chunk_characters_fails()
    {
        var validator = new ChunkingOptionsValidator();
        var options = new ChunkingOptions { Provider = "DoclingJson", MaxChunkCharacters = 0 };

        var result = validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("MaxChunkCharacters", result.FailureMessage);
    }

    // ── Embeddings ────────────────────────────────────────────────

    [Fact]
    public void Mock_embeddings_passes_without_external_config()
    {
        var validator = new OpenAiCompatibleEmbeddingOptionsValidator();
        var options = new OpenAiCompatibleEmbeddingOptions { Provider = "Mock" };

        var result = validator.Validate(null, options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void OpenAICompatible_embeddings_missing_BaseUrl_fails_clearly()
    {
        var validator = new OpenAiCompatibleEmbeddingOptionsValidator();
        var options = new OpenAiCompatibleEmbeddingOptions
        {
            Provider = "OpenAICompatible",
            BaseUrl = "",
            Model = "text-embedding",
            ApiKey = "sk-test"
        };

        var result = validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("BaseUrl", result.FailureMessage);
    }

    [Fact]
    public void OpenAICompatible_embeddings_missing_Model_fails_clearly()
    {
        var validator = new OpenAiCompatibleEmbeddingOptionsValidator();
        var options = new OpenAiCompatibleEmbeddingOptions
        {
            Provider = "OpenAICompatible",
            BaseUrl = "http://localhost:1234/v1",
            Model = "",
            ApiKey = "sk-test"
        };

        var result = validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("Model", result.FailureMessage);
    }

    [Fact]
    public void OpenAICompatible_embeddings_missing_ApiKey_fails_clearly()
    {
        var validator = new OpenAiCompatibleEmbeddingOptionsValidator();
        var options = new OpenAiCompatibleEmbeddingOptions
        {
            Provider = "OpenAICompatible",
            BaseUrl = "http://localhost:1234/v1",
            Model = "text-embedding",
            ApiKey = "",
            ApiKeyEnvironmentVariable = ""
        };

        var result = validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("ApiKey", result.FailureMessage);
    }

    [Fact]
    public void Unknown_embeddings_provider_fails_clearly()
    {
        var validator = new OpenAiCompatibleEmbeddingOptionsValidator();
        var options = new OpenAiCompatibleEmbeddingOptions { Provider = "UnknownEmbedder" };

        var result = validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("not recognized", result.FailureMessage);
    }

    // ── Chat ──────────────────────────────────────────────────────

    [Fact]
    public void Mock_chat_passes_without_external_config()
    {
        var validator = new OpenAiCompatibleChatOptionsValidator();
        var options = new OpenAiCompatibleChatOptions { Provider = "Mock" };

        var result = validator.Validate(null, options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void OpenAICompatible_chat_missing_ApiKey_fails_clearly()
    {
        var validator = new OpenAiCompatibleChatOptionsValidator();
        var options = new OpenAiCompatibleChatOptions
        {
            Provider = "OpenAICompatible",
            BaseUrl = "https://api.deepseek.com/v1",
            Model = "deepseek-chat",
            ApiKey = "",
            ApiKeyEnvironmentVariable = ""
        };

        var result = validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("ApiKey", result.FailureMessage);
    }

    [Fact]
    public void OpenAICompatible_chat_missing_BaseUrl_fails_clearly()
    {
        var validator = new OpenAiCompatibleChatOptionsValidator();
        var options = new OpenAiCompatibleChatOptions
        {
            Provider = "OpenAICompatible",
            BaseUrl = "",
            Model = "deepseek-chat",
            ApiKey = "sk-test"
        };

        var result = validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("BaseUrl", result.FailureMessage);
    }

    // ── Storage ───────────────────────────────────────────────────

    [Fact]
    public void Local_storage_passes_without_external_config()
    {
        var validator = new LocalFileStorageOptionsValidator();
        var options = new LocalFileStorageOptions { Provider = "Local" };

        var result = validator.Validate(null, options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Unknown_storage_provider_fails_clearly()
    {
        var validator = new LocalFileStorageOptionsValidator();
        var options = new LocalFileStorageOptions { Provider = "S3" };

        var result = validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("not recognized", result.FailureMessage);
    }
}
