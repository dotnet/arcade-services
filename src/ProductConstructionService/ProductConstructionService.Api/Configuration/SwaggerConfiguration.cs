﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Reflection;
using Microsoft.AspNetCore.ApiPagination;
using Microsoft.AspNetCore.ApiVersioning.Swashbuckle;
using Microsoft.Net.Http.Headers;
using Microsoft.OpenApi.Models;
using Newtonsoft.Json.Linq;
using ProductConstructionService.Api.Api;

namespace ProductConstructionService.Api.Configuration;

public static class SwaggerConfiguration
{
    public static void ConfigureSwagger(this WebApplicationBuilder builder)
    {
        builder.Services.AddSwaggerApiVersioning(
            (version, info) =>
            {
                info.Description =
                    "The Web API enabling access to the .NET Product Constructions Service that supports the [.NET Core Dependency Flow infrastructure](https://github.com/dotnet/arcade/blob/main/Documentation/DependenciesFlowPlan.md).";
                info.Contact = new OpenApiContact
                {
                    Name = ".NET Product Constructions Services",
                    Email = "dotnetprodconsvcs@microsoft.com",
                };
            });

        builder.Services.AddSwaggerGen(
            options =>
            {
                // If you get an exception saying 'Identical schemaIds detected for types Maestro.Web.Api.<version>.Models.<something> and Maestro.Web.Api.<different-version>.Models.<something>'
                // Then you should NEVER add the following like the StackOverflow answers will suggest.
                // options.CustomSchemaIds(x => x.FullName);

                // This exception means that you have added something to one of the versions of the api that results in a conflict, because one version of the api cannot have 2 models for the same object
                // e.g. If you add a new api, or just a new model to an existing version (with new or modified properties), every method in the API that can return this type,
                // even nested (e.g. you changed Build, and Subscription contains a Build object), must be updated to return the new type.
                // It could also mean that you forgot to apply [ApiRemoved] to an inherited method that shouldn't be included in the new version

                options.FilterOperations(
                    (op, ctx) =>
                    {
                        var errorSchema = ctx.SchemaGenerator.GenerateSchema(typeof(ApiError), ctx.SchemaRepository);
                        op.Responses["default"] = new OpenApiResponse
                        {
                            Description = "Error",
                            Content = new Dictionary<string, OpenApiMediaType>
                            {
                                ["application/json"] = new OpenApiMediaType
                                {
                                    Schema = errorSchema,
                                },
                            },
                        };
                        // Replace the large list generated by WepApi with just application/json
                        // We can accept more than just application/json, but the swagger spec defines what we prefer
                        op.OperationId = $"{op.Tags.First().Name}_{op.OperationId}";
                    });

                options.FilterOperations(
                    (op, ctx) =>
                    {
                        var paginated = ctx.MethodInfo.GetCustomAttribute<PaginatedAttribute>();
                        if (paginated != null)
                        {
                            // Add an extension that tells the client generator that this operation is paged with first,prev,next,last urls in the Link header.
                            op.Extensions["x-ms-paginated"] = new PaginatedExtension
                            {
                                PageParameterName = paginated.PageParameterName,
                                PageSizeParameterName = paginated.PageSizeParameterName
                            };
                        }
                    });

                options.RequestBodyFilter<NameRequestBodyFilter>();

                options.FilterSchemas(
                    (schema, ctx) =>
                    {
                        // Types that are not-nullable in C# should be required
                        if (schema.Type == "object")
                        {
                            var required = schema.Required == null
                                ? []
                                : new HashSet<string>(schema.Required.Select(ToCamelCase));
                            schema.Properties =
                                schema.Properties.ToDictionary(
                                    p => ToCamelCase(p.Key),
                                    p => p.Value);
                            foreach (var property in schema.Properties.Keys)
                            {
                                var propertyInfo = ctx.Type.GetRuntimeProperties().FirstOrDefault(p =>
                                    string.Equals(p.Name, property, StringComparison.OrdinalIgnoreCase));
                                if (propertyInfo != null)
                                {
                                    var propertyType = propertyInfo.PropertyType;
                                    var shouldBeRequired =
                                        propertyType.IsValueType &&
                                        !(propertyType.IsConstructedGenericType &&
                                          propertyType.GetGenericTypeDefinition() == typeof(Nullable<>));
                                    if (shouldBeRequired)
                                    {
                                        required.Add(property);
                                    }
                                }
                            }

                            schema.Required = required;
                        }
                    });

                options.MapType<TimeSpan>(
                    () => new OpenApiSchema
                    {
                        Type = "string",
                        Format = "duration"
                    });
                options.MapType<TimeSpan?>(
                    () => new OpenApiSchema
                    {
                        Type = "string",
                        Format = "duration"
                    });
                options.MapType<JToken>(() => new OpenApiSchema());

                options.DescribeAllParametersInCamelCase();

                string xmlPath;
                if (builder.Environment.IsDevelopment())
                {
                    xmlPath = Path.GetDirectoryName(typeof(PcsStartup).Assembly.Location)!;
                }
                else
                {
                    xmlPath = builder.Environment.ContentRootPath;
                }

                string assemblyName = typeof(SwaggerConfiguration).Assembly.GetName().Name!;
                string xmlFile = Path.Combine(xmlPath, assemblyName + ".xml");
                if (File.Exists(xmlFile))
                {
                    options.IncludeXmlComments(xmlFile);
                }

                options.AddSecurityDefinition(
                    "Bearer",
                    new OpenApiSecurityScheme
                    {
                        Type = SecuritySchemeType.ApiKey,
                        Name = HeaderNames.Authorization,
                        In = ParameterLocation.Header,
                        Scheme = "bearer",
                        Description = """
                            JWT Authorization header using the Bearer scheme. 
                            Enter 'Bearer ' and then your token in the text input below.
                            Example: 'Bearer 12345abcdef'
                            """,
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
        builder.Services.AddSwaggerGenNewtonsoftSupport();
    }

    private static string ToCamelCase(string value)
    {
        return string.Concat(value.Substring(0, 1).ToLowerInvariant(), value.AsSpan(1));
    }
}
