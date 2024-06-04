// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Net.Http.Headers;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.Swagger;

namespace ProductConstructionService.Api.Configuration;

public static class SwaggerConfiguration
{
    public static void ConfigureSwagger(this WebApplicationBuilder builder)
    {
        builder.Services.AddSwaggerGen(options =>
        {
            options.AddSecurityDefinition(
                "Bearer",
                new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.ApiKey,
                    Name = HeaderNames.Authorization,
                    In = ParameterLocation.Header,
                    Scheme = "bearer",
                    Description = @"JWT Authorization header using the Bearer scheme. \r\n\r\n 
                      Enter 'Bearer ' and then your token in the text input below.
                      \r\n\r\nExample: 'Bearer 12345abcdef'",
                });

            options.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Name = "Bearer",
                        Reference = new OpenApiReference
                        {
                            Id = "Bearer",
                            Type = ReferenceType.SecurityScheme,
                        },
                        In = ParameterLocation.Header,
                    },
                    []
                }
            });
        });

        // These settings help with automated C# client generation
        builder.Services.Configure<SwaggerOptions>(options =>
        {
            options.SerializeAsV2 = false;
            options.PreSerializeFilters.Add(
                (doc, req) =>
                {
                    doc.Servers =
                    [
                        new()
                        {
                            Url = $"{req.Scheme}://{req.Host.Value}/",
                        },
                    ];
                });
        });
    }
}
