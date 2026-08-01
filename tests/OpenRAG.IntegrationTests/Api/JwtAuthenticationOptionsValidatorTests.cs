using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OpenRAG.Api.Security;

namespace OpenRAG.IntegrationTests.Api;

public sealed class JwtAuthenticationOptionsValidatorTests
{
    private readonly JwtAuthenticationOptionsValidator _validator = new();

    [Fact]
    public void Rejects_missing_authority()
    {
        var options = ValidOptions();
        options.Authority = "";

        AssertInvalid(options, "Authority is required");
    }

    [Fact]
    public void Rejects_malformed_authority()
    {
        var options = ValidOptions();
        options.Authority = "not-a-uri";

        AssertInvalid(options, "absolute URI");
    }

    [Fact]
    public void Rejects_http_authority_when_https_metadata_is_required()
    {
        var options = ValidOptions();
        options.Authority = "http://identity.example.invalid";

        AssertInvalid(options, "must use HTTPS");
    }

    [Fact]
    public void Allows_http_authority_only_when_https_metadata_is_explicitly_disabled()
    {
        var options = ValidOptions();
        options.Authority = "http://identity.example.invalid";
        options.RequireHttpsMetadata = false;

        var result = _validator.Validate(null, options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Rejects_missing_audience()
    {
        var options = ValidOptions();
        options.Audience = "";

        AssertInvalid(options, "Audience is required");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Rejects_blank_claim_names(bool blankUserIdClaim)
    {
        var options = ValidOptions();
        options.UserIdClaimType = blankUserIdClaim ? " " : OpenRagClaimTypes.UserId;
        options.RoleClaimType = blankUserIdClaim ? OpenRagClaimTypes.Role : " ";

        AssertInvalid(options, "cannot be blank");
    }

    [Fact]
    public void Rejects_negative_clock_skew()
    {
        var options = ValidOptions();
        options.ClockSkewSeconds = -1;

        AssertInvalid(options, "cannot be negative");
    }

    [Fact]
    public void Rejects_excessive_clock_skew()
    {
        var options = ValidOptions();
        options.ClockSkewSeconds = 301;

        AssertInvalid(options, "cannot exceed");
    }

    [Fact]
    public void Accepts_valid_configuration()
    {
        var result = _validator.Validate(null, ValidOptions());

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Defaults_are_secure_and_predictable()
    {
        var options = new JwtAuthenticationOptions();

        Assert.True(options.RequireHttpsMetadata);
        Assert.Equal(OpenRagClaimTypes.UserId, options.UserIdClaimType);
        Assert.Equal(OpenRagClaimTypes.Role, options.RoleClaimType);
        Assert.Equal(60, options.ClockSkewSeconds);
    }

    [Fact]
    public async Task Missing_required_configuration_fails_during_host_startup()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration[$"{JwtAuthenticationOptions.SectionName}:Authority"] = "";
        builder.Configuration[$"{JwtAuthenticationOptions.SectionName}:Audience"] = "";
        builder.Services.AddOpenRagAuthentication(builder.Configuration);
        using var host = builder.Build();

        var exception = await Assert.ThrowsAsync<OptionsValidationException>(
            () => host.StartAsync(CancellationToken.None));

        Assert.Contains("Authority is required", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Audience is required", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private void AssertInvalid(JwtAuthenticationOptions options, string expectedFailure)
    {
        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(
            result.Failures,
            failure => failure.Contains(expectedFailure, StringComparison.OrdinalIgnoreCase));
    }

    private static JwtAuthenticationOptions ValidOptions() => new()
    {
        Authority = AuthenticatedApiWebApplicationFactory.Issuer,
        Audience = AuthenticatedApiWebApplicationFactory.Audience,
        RequireHttpsMetadata = true,
        UserIdClaimType = OpenRagClaimTypes.UserId,
        RoleClaimType = OpenRagClaimTypes.Role,
        ClockSkewSeconds = 60
    };
}
