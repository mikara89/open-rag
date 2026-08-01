using Microsoft.Extensions.Options;

namespace OpenRAG.Api.Security;

public sealed class JwtAuthenticationOptionsValidator : IValidateOptions<JwtAuthenticationOptions>
{
    public ValidateOptionsResult Validate(string? name, JwtAuthenticationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();
        Uri? authority = null;

        if (string.IsNullOrWhiteSpace(options.Authority))
        {
            failures.Add($"{JwtAuthenticationOptions.SectionName}:Authority is required.");
        }
        else if (!Uri.TryCreate(options.Authority, UriKind.Absolute, out authority))
        {
            failures.Add($"{JwtAuthenticationOptions.SectionName}:Authority must be an absolute URI.");
        }

        if (authority is not null)
        {
            var isHttp = string.Equals(
                authority.Scheme,
                Uri.UriSchemeHttp,
                StringComparison.OrdinalIgnoreCase);
            var isHttps = string.Equals(
                authority.Scheme,
                Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase);

            if (!isHttp && !isHttps)
            {
                failures.Add(
                    $"{JwtAuthenticationOptions.SectionName}:Authority must use HTTP or HTTPS.");
            }
            else if (options.RequireHttpsMetadata && !isHttps)
            {
                failures.Add(
                    $"{JwtAuthenticationOptions.SectionName}:Authority must use HTTPS when RequireHttpsMetadata is true.");
            }
        }

        if (string.IsNullOrWhiteSpace(options.Audience))
        {
            failures.Add($"{JwtAuthenticationOptions.SectionName}:Audience is required.");
        }

        if (string.IsNullOrWhiteSpace(options.UserIdClaimType))
        {
            failures.Add($"{JwtAuthenticationOptions.SectionName}:UserIdClaimType cannot be blank.");
        }

        if (string.IsNullOrWhiteSpace(options.RoleClaimType))
        {
            failures.Add($"{JwtAuthenticationOptions.SectionName}:RoleClaimType cannot be blank.");
        }

        if (options.ClockSkewSeconds < 0)
        {
            failures.Add($"{JwtAuthenticationOptions.SectionName}:ClockSkewSeconds cannot be negative.");
        }
        else if (options.ClockSkewSeconds > JwtAuthenticationOptions.MaximumClockSkewSeconds)
        {
            failures.Add(
                $"{JwtAuthenticationOptions.SectionName}:ClockSkewSeconds cannot exceed " +
                $"{JwtAuthenticationOptions.MaximumClockSkewSeconds} seconds.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
