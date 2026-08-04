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
using OpenRAG.Api.Security;
using OpenRAG.Application.Abstractions.AI;
using OpenRAG.Application.Abstractions.Messaging;
using OpenRAG.Application.Abstractions.Processing;

namespace OpenRAG.LiveIntegrationTests.Infrastructure;

internal sealed class LiveApiFactory : WebApplicationFactory<OpenRAG.Api.AssemblyReference>
{
    public const string Issuer = "https://live-tests.identity.example.invalid";
    public const string Audience = "openrag-live-tests";

    private readonly IReadOnlyDictionary<string, string?> _configuration;
    private readonly LiveProviderProbe _probe;
    private readonly CapturingDocumentEventBus _eventBus;
    private readonly SymmetricSecurityKey _signingKey = new(RandomNumberGenerator.GetBytes(32))
    {
        KeyId = Guid.NewGuid().ToString("N")
    };

    public LiveApiFactory(
        IReadOnlyDictionary<string, string?> configuration,
        LiveProviderProbe probe,
        CapturingDocumentEventBus eventBus)
    {
        _configuration = configuration;
        _probe = probe;
        _eventBus = eventBus;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // A dedicated environment prevents appsettings.Development.json from
        // overriding the per-container connection string added below.
        builder.UseEnvironment("LiveIntegrationTests");
        foreach (var setting in _configuration)
        {
            if (setting.Value is not null)
                builder.UseSetting(setting.Key, setting.Value);
        }
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(_configuration));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IHostedService>();
            ReplaceExternalBoundaries(services, _probe, _eventBus);
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

    public HttpClient CreateAuthenticatedClient(Guid userId, Guid tenantId, bool administrator = false)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
        var claims = new List<Claim>
        {
            new(OpenRagClaimTypes.UserId, userId.ToString("D")),
            new(OpenRagClaimTypes.TenantId, tenantId.ToString("D"))
        };
        if (administrator)
            claims.Add(new Claim(OpenRagClaimTypes.Role, OpenRagRoles.Administrator));

        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", CreateToken(claims));
        return client;
    }

    public HttpClient CreateClientWithClaims(IEnumerable<Claim> claims)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", CreateToken(claims));
        return client;
    }

    public string CreateToken(IEnumerable<Claim> claims)
    {
        var expiration = DateTime.UtcNow.AddMinutes(10);
        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: claims,
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: expiration,
            signingCredentials: new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    internal static void ReplaceExternalBoundaries(
        IServiceCollection services,
        LiveProviderProbe probe,
        CapturingDocumentEventBus eventBus)
    {
        services.RemoveAll<IDocumentPreprocessor>();
        services.RemoveAll<IEmbeddingService>();
        services.RemoveAll<IChatCompletionService>();
        services.RemoveAll<IDocumentIntelligenceService>();
        services.RemoveAll<IDocumentEventBus>();

        services.AddSingleton(probe);
        services.AddScoped<IDocumentPreprocessor, DeterministicDocumentPreprocessor>();
        services.AddSingleton<IEmbeddingService, DeterministicEmbeddingService>();
        services.AddSingleton<IChatCompletionService, DeterministicChatCompletionService>();
        services.AddSingleton<IDocumentIntelligenceService, DeterministicIntelligenceService>();
        services.AddSingleton<IDocumentEventBus>(eventBus);
    }
}
