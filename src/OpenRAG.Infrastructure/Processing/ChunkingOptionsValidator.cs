using Microsoft.Extensions.Options;

namespace OpenRAG.Infrastructure.Processing;

public sealed class ChunkingOptionsValidator : IValidateOptions<ChunkingOptions>
{
    public ValidateOptionsResult Validate(string? name, ChunkingOptions options)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Provider))
        {
            errors.Add("Chunking:Provider must not be empty.");
        }

        var provider = options.Provider ?? "";

        if (string.Equals(provider, "DoclingJson", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(provider, "SimpleMarkdown", StringComparison.OrdinalIgnoreCase))
        {
            // Both require no external configuration beyond chunk settings
            if (options.MaxChunkCharacters <= 0)
                errors.Add("Chunking:MaxChunkCharacters must be positive.");
        }
        else
        {
            errors.Add($"Chunking:Provider '{options.Provider}' is not recognized. Valid providers: DoclingJson, SimpleMarkdown.");
        }

        if (errors.Count > 0)
            return ValidateOptionsResult.Fail(errors);

        return ValidateOptionsResult.Success;
    }
}
