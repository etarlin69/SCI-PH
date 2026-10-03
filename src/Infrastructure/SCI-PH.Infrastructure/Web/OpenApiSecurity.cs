using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi.Models;
using SCI_PH.Infrastructure.Security;

namespace SCI_PH.Infrastructure.Web
{
    // Declares the security scheme of each host and marks only protected operations with it.
    // Anonymous endpoints (login, refresh, activation, recovery, health) get no requirement.
    public static class OpenApiSecurity
    {
        public const string BearerSchemeName = "Bearer";

        public static OpenApiOptions AddBearerSecurity(this OpenApiOptions options) =>
            options.AddSecuritySchemes(new Dictionary<string, OpenApiSecurityScheme>
            {
                [BearerSchemeName] = new()
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT",
                    Description = "Access token from /auth/login (Authorization: Bearer <token>)."
                }
            });

        public static OpenApiOptions AddDeviceKeySecurity(this OpenApiOptions options) =>
            options.AddSecuritySchemes(new Dictionary<string, OpenApiSecurityScheme>
            {
                [DeviceAuthenticationDefaults.DeviceIdHeader] = ApiKey(DeviceAuthenticationDefaults.DeviceIdHeader, "Registered device code."),
                [DeviceAuthenticationDefaults.DeviceKeyHeader] = ApiKey(DeviceAuthenticationDefaults.DeviceKeyHeader, "Device key issued once at provisioning. HTTPS only.")
            });

        private static OpenApiSecurityScheme ApiKey(string header, string description) =>
            new() { Type = SecuritySchemeType.ApiKey, In = ParameterLocation.Header, Name = header, Description = description };

        private static OpenApiOptions AddSecuritySchemes(this OpenApiOptions options, IReadOnlyDictionary<string, OpenApiSecurityScheme> schemes)
        {
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Components ??= new OpenApiComponents();
                foreach (var (name, scheme) in schemes)
                {
                    document.Components.SecuritySchemes[name] = scheme;
                }

                return Task.CompletedTask;
            });

            options.AddOperationTransformer((operation, context, _) =>
            {
                var metadata = context.Description.ActionDescriptor.EndpointMetadata;
                if (metadata.OfType<IAllowAnonymous>().Any())
                {
                    return Task.CompletedTask;
                }

                var requirement = new OpenApiSecurityRequirement();
                foreach (var name in schemes.Keys)
                {
                    requirement[new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = name }
                    }] = [];
                }

                operation.Security.Add(requirement);
                operation.Responses.TryAdd("401", new OpenApiResponse { Description = "Missing or invalid credentials." });
                if (metadata.OfType<IAuthorizeData>().Any(data => !string.IsNullOrEmpty(data.Policy)))
                {
                    operation.Responses.TryAdd("403", new OpenApiResponse { Description = "Authenticated without the required permission." });
                }

                return Task.CompletedTask;
            });

            return options;
        }
    }
}
