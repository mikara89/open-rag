using Microsoft.Extensions.Options;

namespace OpenRAG.Infrastructure.AI;

public sealed class OpenAiCompatibleEmbeddingOptionsValidator : IValidateOptions<OpenAiCompatibleEmbeddingOptions>
{
    public ValidateOptionsResult Validate(string? name, OpenAiCompatibleEmbeddingOptions options)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Provider))
        {
            errors.Add("AI:Embeddings:Provider must not be empty.");
        }

        var provider = options.Provider ?? "";

        if (string.Equals(provider, "OpenAICompatible", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(options.BaseUrl))
                errors.Add("AI:Embeddings:BaseUrl is required when Provider is OpenAICompatible.");

            if (string.IsNullOrWhiteSpace(options.Model))
                errors.Add("AI:Embeddings:Model is required when Provider is OpenAICompatible.");

            // API key or environment variable must be specified
            var hasApiKey = !string.IsNullOrWhiteSpace(options.ApiKey);
            var hasApiKeyEnvVar = !string.IsNullOrWhiteSpace(options.ApiKeyEnvironmentVariable);
            var hasConventionalEnvVar = !string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable("OPENAI_API_KEY") ??
                Environment.GetEnvironmentVariable("OPENRAG_EMBEDDINGS_API_KEY"));

            if (!hasApiKey && !hasApiKeyEnvVar && !hasConventionalEnvVar)
                errors.Add("AI:Embeddings:ApiKey or ApiKeyEnvironmentVariable is required when Provider is OpenAICompatible. " +
                           "Set ApiKey directly, or specify ApiKeyEnvironmentVariable (e.g., OPENAI_API_KEY, OPENRAG_EMBEDDINGS_API_KEY).");
        }
        else if (string.Equals(provider, "Mock", StringComparison.OrdinalIgnoreCase))
        {
            // Mock requires no external configuration
        }
        else
        {
            errors.Add($"AI:Embeddings:Provider '{options.Provider}' is not recognized. Valid providers: OpenAICompatible, Mock.");
        }

        if (errors.Count > 0)
            return ValidateOptionsResult.Fail(errors);

        return ValidateOptionsResult.Success;
    }
}
