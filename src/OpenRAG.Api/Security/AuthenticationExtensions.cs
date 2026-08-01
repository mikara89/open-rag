using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpenRAG.Application.Abstractions.Security;

namespace OpenRAG.Api.Security;

public static class AuthenticationExtensions
{
    public static IServiceCollection AddOpenRagAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<JwtAuthenticationOptions>()
            .Bind(configuration.GetSection(JwtAuthenticationOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<
            IValidateOptions<JwtAuthenticationOptions>,
            JwtAuthenticationOptionsValidator>();

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();

        services
            .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtAuthenticationOptions>>((bearer, configured) =>
            {
                var jwt = configured.Value;
                bearer.Authority = jwt.Authority;
                bearer.Audience = jwt.Audience;
                bearer.RequireHttpsMetadata = jwt.RequireHttpsMetadata;
                bearer.MapInboundClaims = false;
                bearer.IncludeErrorDetails = false;
                bearer.SaveToken = false;
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateIssuerSigningKey = true,
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    RequireSignedTokens = true,
                    RoleClaimType = jwt.RoleClaimType,
                    ClockSkew = TimeSpan.FromSeconds(jwt.ClockSkewSeconds)
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(OpenRagPolicies.AuthenticatedUser, policy =>
            {
                policy.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme);
                policy.RequireAuthenticatedUser();
                policy.AddRequirements(new ValidUserIdentityRequirement());
            })
            .AddPolicy(OpenRagPolicies.Administrator, policy =>
            {
                policy.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme);
                policy.RequireAuthenticatedUser();
                policy.AddRequirements(new ValidUserIdentityRequirement());
                policy.RequireRole(OpenRagRoles.Administrator);
            });

        services.AddSingleton<IAuthorizationHandler, ValidUserIdentityHandler>();
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpContextCurrentUser>();

        return services;
    }
}
