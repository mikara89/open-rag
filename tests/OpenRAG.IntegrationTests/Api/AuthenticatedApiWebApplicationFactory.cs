using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using OpenRAG.Api;
using OpenRAG.Api.Security;

namespace OpenRAG.IntegrationTests.Api;

public sealed class AuthenticatedApiWebApplicationFactory : WebApplicationFactory<AssemblyReference>
{
    public const string Issuer = "https://identity.example.invalid";
    public const string Audience = "openrag-api";

    private readonly SymmetricSecurityKey _signingKey = new(RandomNumberGenerator.GetBytes(32))
    {
        KeyId = Guid.NewGuid().ToString("N")
    };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:openrag-db"] =
                    "Host=localhost;Port=5432;Database=openrag_auth_test;Username=test;Password=test",
                ["ConnectionStrings:rabbitmq"] = "amqp://guest:guest@localhost:5672/",
                ["Storage:LocalRootPath"] = Path.Combine(Path.GetTempPath(), "openrag-auth-tests"),
                ["Preprocessing:Docling:Provider"] = "Mock",
                ["AI:Embeddings:Provider"] = "Mock",
                ["AI:Chat:Provider"] = "Mock",
                ["Intelligence:Provider"] = "Mock",
                [$"{JwtAuthenticationOptions.SectionName}:Authority"] = Issuer,
                [$"{JwtAuthenticationOptions.SectionName}:Audience"] = Audience,
                [$"{JwtAuthenticationOptions.SectionName}:RequireHttpsMetadata"] = "true",
                [$"{JwtAuthenticationOptions.SectionName}:UserIdClaimType"] = OpenRagClaimTypes.UserId,
                [$"{JwtAuthenticationOptions.SectionName}:RoleClaimType"] = OpenRagClaimTypes.Role,
                [$"{JwtAuthenticationOptions.SectionName}:ClockSkewSeconds"] = "60"
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.PostConfigure<JwtBearerOptions>(
                JwtBearerDefaults.AuthenticationScheme,
                options =>
                {
                    var configuration = new OpenIdConnectConfiguration
                    {
                        Issuer = Issuer,
                        SigningKeys = { _signingKey }
                    };
                    options.Configuration = configuration;
                    options.ConfigurationManager =
                        new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
                });
        });
    }

    public HttpClient CreateHttpsClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        BaseAddress = new Uri("https://localhost")
    });

    public string CreateToken(
        IEnumerable<Claim>? claims = null,
        string? issuer = null,
        string? audience = null,
        DateTime? expires = null,
        SecurityKey? signingKey = null,
        bool includeExpiration = true,
        bool signToken = true)
    {
        var expiration = expires ?? DateTime.UtcNow.AddMinutes(5);
        var token = new JwtSecurityToken(
            issuer: issuer ?? Issuer,
            audience: audience ?? Audience,
            claims: claims,
            notBefore: expiration <= DateTime.UtcNow
                ? expiration.AddMinutes(-5)
                : DateTime.UtcNow.AddMinutes(-1),
            expires: includeExpiration ? expiration : null,
            signingCredentials: signToken
                ? new SigningCredentials(
                    signingKey ?? _signingKey,
                    SecurityAlgorithms.HmacSha256)
                : null);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
