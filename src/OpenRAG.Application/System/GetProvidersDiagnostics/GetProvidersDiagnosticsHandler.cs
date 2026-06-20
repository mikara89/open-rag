using Microsoft.Extensions.Configuration;

namespace OpenRAG.Application.System.GetProvidersDiagnostics;

public sealed class GetProvidersDiagnosticsHandler
    : Mediator.IRequestHandler<GetProvidersDiagnosticsQuery, GetProvidersDiagnosticsResponse>
{
    private readonly IConfiguration _configuration;

    public GetProvidersDiagnosticsHandler(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public ValueTask<GetProvidersDiagnosticsResponse> Handle(
        GetProvidersDiagnosticsQuery query,
        CancellationToken cancellationToken)
    {
        var preprocessing = BuildPreprocessingDiagnostics();
        var chunking = BuildChunkingDiagnostics();
        var embeddings = BuildEmbeddingsDiagnostics();
        var chat = BuildChatDiagnostics();
        var storage = BuildStorageDiagnostics();

        return ValueTask.FromResult(new GetProvidersDiagnosticsResponse(
            Preprocessing: preprocessing,
            Chunking: chunking,
            Embeddings: embeddings,
            Chat: chat,
            Storage: storage));
    }

    private ProviderDiagnostics BuildPreprocessingDiagnostics()
    {
        var section = _configuration.GetSection("Preprocessing:Docling");
        var provider = section["Provider"] ?? "Mock";
        var baseUrl = section["BaseUrl"];
        var convertFilePath = section["ConvertFilePath"];
        var errors = ValidatePreprocessing(provider, baseUrl, convertFilePath);

        return new ProviderDiagnostics(
            Provider: provider,
            Configured: errors.Count == 0,
            BaseUrl: baseUrl,
            ConvertFilePath: convertFilePath,
            TimeoutSeconds: ParseInt(section["TimeoutSeconds"]),
            ValidationErrors: errors.Count > 0 ? errors : null);
    }

    private ProviderDiagnostics BuildChunkingDiagnostics()
    {
        var section = _configuration.GetSection("Chunking");
        var provider = section["Provider"] ?? "DoclingJson";

        return new ProviderDiagnostics(
            Provider: provider,
            Configured: true,
            MaxChunkCharacters: ParseInt(section["MaxChunkCharacters"]),
            OverlapCharacters: ParseInt(section["OverlapCharacters"]));
    }

    private ProviderDiagnostics BuildEmbeddingsDiagnostics()
    {
        var section = _configuration.GetSection("AI:Embeddings");
        var provider = section["Provider"] ?? "Mock";
        var baseUrl = section["BaseUrl"];
        var model = section["Model"];

        var configApiKey = section["ApiKey"];
        var apiKeyEnvVar = section["ApiKeyEnvironmentVariable"];
        var resolvedKey = ResolveApiKey(configApiKey, apiKeyEnvVar,
            ["OPENAI_API_KEY", "OPENRAG_EMBEDDINGS_API_KEY"]);
        var keyPresent = !string.IsNullOrWhiteSpace(resolvedKey);

        return new ProviderDiagnostics(
            Provider: provider,
            Configured: true,
            BaseUrl: baseUrl,
            Model: model,
            ApiKeyPresent: keyPresent,
            TimeoutSeconds: ParseInt(section["TimeoutSeconds"]),
            Dimensions: ParseInt(section["Dimensions"]));
    }

    private ProviderDiagnostics BuildChatDiagnostics()
    {
        var section = _configuration.GetSection("AI:Chat");
        var provider = section["Provider"] ?? "Mock";
        var baseUrl = section["BaseUrl"];
        var model = section["Model"];

        var configApiKey = section["ApiKey"];
        var apiKeyEnvVar = section["ApiKeyEnvironmentVariable"];
        var resolvedKey = ResolveApiKey(configApiKey, apiKeyEnvVar,
            ["OPENAI_API_KEY", "DEEPSEEK_API_KEY", "OPENRAG_CHAT_API_KEY"]);
        var keyPresent = !string.IsNullOrWhiteSpace(resolvedKey);

        return new ProviderDiagnostics(
            Provider: provider,
            Configured: true,
            BaseUrl: baseUrl,
            Model: model,
            ApiKeyPresent: keyPresent,
            TimeoutSeconds: ParseInt(section["TimeoutSeconds"]));
    }

    private ProviderDiagnostics BuildStorageDiagnostics()
    {
        var section = _configuration.GetSection("Storage");
        var provider = section["Provider"] ?? "Local";

        return new ProviderDiagnostics(
            Provider: provider,
            Configured: true,
            LocalRootPath: section["LocalRootPath"]);
    }

    // ── Validation ────────────────────────────────────────────────

    private static List<string> ValidatePreprocessing(
        string? provider, string? baseUrl, string? convertFilePath)
    {
        var errors = new List<string>();

        if (string.Equals(provider, "DoclingServe", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
                errors.Add("Preprocessing:Docling:BaseUrl is required when Provider is DoclingServe.");

            if (string.IsNullOrWhiteSpace(convertFilePath))
                errors.Add("Preprocessing:Docling:ConvertFilePath is required when Provider is DoclingServe.");
        }
        else if (!string.Equals(provider, "Mock", StringComparison.OrdinalIgnoreCase) &&
                 !string.IsNullOrWhiteSpace(provider))
        {
            errors.Add($"Preprocessing:Docling:Provider '{provider}' is not recognized. Valid providers: DoclingServe, Mock.");
        }

        return errors;
    }

    // ── Secure key resolution ─────────────────────────────────────

    private static string? ResolveApiKey(
        string? configApiKey,
        string? apiKeyEnvironmentVariable,
        string[] conventionalEnvVarNames)
    {
        if (!string.IsNullOrWhiteSpace(configApiKey) && !IsPlaceholder(configApiKey))
            return configApiKey;

        if (!string.IsNullOrWhiteSpace(apiKeyEnvironmentVariable))
        {
            var envValue = Environment.GetEnvironmentVariable(apiKeyEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(envValue))
                return envValue;
        }

        foreach (var envVarName in conventionalEnvVarNames)
        {
            var envValue = Environment.GetEnvironmentVariable(envVarName);
            if (!string.IsNullOrWhiteSpace(envValue))
                return envValue;
        }

        return null;
    }

    private static bool IsPlaceholder(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0) return true;
        if (trimmed.Equals("YOUR_DEEPSEEK_API_KEY_HERE", StringComparison.OrdinalIgnoreCase)) return true;
        if (trimmed.Equals("<from secrets>", StringComparison.OrdinalIgnoreCase)) return true;
        if (trimmed.Equals("YOUR_API_KEY_HERE", StringComparison.OrdinalIgnoreCase)) return true;
        if (trimmed.Equals("not-needed-for-local", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static int? ParseInt(string? value) =>
        int.TryParse(value, out var result) ? result : null;
}
