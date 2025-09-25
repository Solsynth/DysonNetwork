using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;

namespace DysonNetwork.Shared.Http;

public static class SwaggerGen
{
    public static WebApplicationBuilder AddSwaggerManifest(
        this WebApplicationBuilder builder,
        string serviceName,
        string? serviceDescription
    )
    {
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Version = "v1",
                Title = serviceName,
                Description = serviceDescription,
                TermsOfService = new Uri("https://solsynth.dev/terms"),
                License = new OpenApiLicense
                {
                    Name = "APGLv3",
                    Url = new Uri("https://www.gnu.org/licenses/agpl-3.0.html")
                }
            });
            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                In = ParameterLocation.Header,
                Description = "Solar Network Unified Authentication",
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                BearerFormat = "JWT",
                Scheme = "Bearer"
            });
            options.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.SecurityScheme,
                            Id = "Bearer"
                        }
                    },
                    []
                }
            });
        });
        builder.Services.AddOpenApi();

        return builder;
    }

    public static WebApplication UseSwaggerManifest(this WebApplication app)
    {
        app.MapOpenApi();
        
        var configuration = app.Configuration;
        app.UseSwagger(c =>
        {
            c.PreSerializeFilters.Add((swaggerDoc, httpReq) =>
            {
                var publicBasePath = configuration["Swagger:PublicBasePath"]?.TrimEnd('/') ?? "";

                // 1. Adjust servers
                swaggerDoc.Servers = new List<OpenApiServer>
                {
                    new() { Url = publicBasePath }
                };

                // 2. Rewrite all path keys (remove /api or replace it)
                var newPaths = new OpenApiPaths();
                foreach (var (path, pathItem) in swaggerDoc.Paths)
                {
                    // e.g. original path = "/api/drive/chunk/{taskId}/{chunkIndex}"
                    // We want to produce "/sphere/drive/chunk/{taskId}/{chunkIndex}" or maybe "/sphere/chunk/..."
                    var newPathKey = path;

                    // If "path" starts with "/api", strip it
                    if (newPathKey.StartsWith("/api", StringComparison.OrdinalIgnoreCase))
                    {
                        newPathKey = newPathKey["/api".Length..];
                        if (!newPathKey.StartsWith("/"))
                            newPathKey = "/" + newPathKey;
                    }

                    // Then prepend the public base path (if not root)
                    if (!string.IsNullOrEmpty(publicBasePath) && publicBasePath != "/")
                    {
                        // ensure slash composition
                        newPathKey = publicBasePath.TrimEnd('/') + newPathKey;
                    }

                    newPaths.Add(newPathKey, pathItem);
                }

                swaggerDoc.Paths = newPaths;
            });
        });

        app.UseSwaggerUI(options =>
        {
            // Swagger UI must point to the JSON location
            var publicBasePath = configuration["Swagger:PublicBasePath"]?.TrimEnd('/') ?? "";
            options.SwaggerEndpoint(
                $"{publicBasePath}/swagger/v1/swagger.json",
                "Develop API v1");
        });

        return app;
    }
}