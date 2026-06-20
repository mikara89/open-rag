using Microsoft.Extensions.Options;

namespace OpenRAG.Infrastructure.Preprocessing;

public sealed class DoclingPreprocessorOptionsValidator : IValidateOptions<DoclingPreprocessorOptions>
{
    public ValidateOptionsResult Validate(string? name, DoclingPreprocessorOptions options)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Provider))
        {
            errors.Add("Preprocessing:Docling:Provider must not be empty.");
        }

        var provider = options.Provider ?? "";

        if (string.Equals(provider, "DoclingServe", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(options.BaseUrl))
                errors.Add("Preprocessing:Docling:BaseUrl is required when Provider is DoclingServe.");

            if (string.IsNullOrWhiteSpace(options.ConvertFilePath))
                errors.Add("Preprocessing:Docling:ConvertFilePath is required when Provider is DoclingServe.");
        }
        else if (string.Equals(provider, "Mock", StringComparison.OrdinalIgnoreCase))
        {
            // Mock requires no external configuration
        }
        else
        {
            errors.Add($"Preprocessing:Docling:Provider '{options.Provider}' is not recognized. Valid providers: DoclingServe, Mock.");
        }

        if (errors.Count > 0)
            return ValidateOptionsResult.Fail(errors);

        return ValidateOptionsResult.Success;
    }
}
