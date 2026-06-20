using Microsoft.Extensions.Logging;

namespace OpenRAG.Infrastructure.AI;

/// <summary>
/// Securely resolves API keys from configuration or environment variables.
/// Supports explicit config values, named environment variables, and conventional
/// environment variables (OPENAI_API_KEY, DEEPSEEK_API_KEY, etc.).
/// Never logs full key values.
/// </summary>
public static class SecureApiKeyResolver
{
    /// <summary>
    /// Resolves an API key using the following priority:
    /// 1. Explicit config value (unless it looks like a placeholder)
    /// 2. Environment variable specified by <paramref name="apiKeyEnvironmentVariable"/>
    /// 3. Conventional environment variables
    /// </summary>
    public static string? ResolveApiKey(
        string? configApiKey,
        string? apiKeyEnvironmentVariable,
        string[] conventionalEnvVarNames,
        ILogger? logger = null)
    {
        // 1. Explicit config value (skip obvious placeholders)
        if (!string.IsNullOrWhiteSpace(configApiKey) &&
            !IsPlaceholder(configApiKey))
        {
            logger?.LogInformation("Using API key from configuration");
            return configApiKey;
        }

        // 2. Named environment variable from config
        if (!string.IsNullOrWhiteSpace(apiKeyEnvironmentVariable))
        {
            var envValue = Environment.GetEnvironmentVariable(apiKeyEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(envValue))
            {
                logger?.LogInformation(
                    "Using API key from environment variable {EnvVarName}",
                    apiKeyEnvironmentVariable);
                return envValue;
            }
            logger?.LogWarning(
                "Environment variable {EnvVarName} was specified but is empty or not set",
                apiKeyEnvironmentVariable);
        }

        // 3. Conventional environment variables
        foreach (var envVarName in conventionalEnvVarNames)
        {
            var envValue = Environment.GetEnvironmentVariable(envVarName);
            if (!string.IsNullOrWhiteSpace(envValue))
            {
                logger?.LogInformation(
                    "Using API key from conventional environment variable {EnvVarName}",
                    envVarName);
                return envValue;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns a safe representation: "present" or "missing".
    /// </summary>
    public static string KeyStatus(string? apiKey) =>
        string.IsNullOrWhiteSpace(apiKey) ? "missing" : "present";

    private static bool IsPlaceholder(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0) return true;

        // Common placeholder patterns
        if (trimmed.Equals("YOUR_DEEPSEEK_API_KEY_HERE", StringComparison.OrdinalIgnoreCase)) return true;
        if (trimmed.Equals("<from secrets>", StringComparison.OrdinalIgnoreCase)) return true;
        if (trimmed.Equals("YOUR_API_KEY_HERE", StringComparison.OrdinalIgnoreCase)) return true;
        if (trimmed.Equals("not-needed-for-local", StringComparison.OrdinalIgnoreCase)) return true;

        return false;
    }
}
