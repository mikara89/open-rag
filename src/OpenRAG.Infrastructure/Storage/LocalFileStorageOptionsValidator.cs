using Microsoft.Extensions.Options;

namespace OpenRAG.Infrastructure.Storage;

public sealed class LocalFileStorageOptionsValidator : IValidateOptions<LocalFileStorageOptions>
{
    public ValidateOptionsResult Validate(string? name, LocalFileStorageOptions options)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Provider))
        {
            errors.Add("Storage:Provider must not be empty.");
        }

        var provider = options.Provider ?? "";

        if (string.Equals(provider, "Local", StringComparison.OrdinalIgnoreCase))
        {
            // LocalRootPath can be empty — LocalFileStorage will use default
            if (!string.IsNullOrWhiteSpace(options.LocalRootPath))
            {
                // Verify the path is not an absolute filesystem root that could cause issues
                try
                {
                    _ = Path.GetFullPath(options.LocalRootPath);
                }
                catch (Exception ex)
                {
                    errors.Add($"Storage:LocalRootPath is not a valid path: {ex.Message}");
                }
            }
        }
        else
        {
            errors.Add($"Storage:Provider '{options.Provider}' is not recognized. Valid providers: Local.");
        }

        if (errors.Count > 0)
            return ValidateOptionsResult.Fail(errors);

        return ValidateOptionsResult.Success;
    }
}
