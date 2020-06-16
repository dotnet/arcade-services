// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Microsoft.AspNetCore.ApiVersioning.Swashbuckle
{
    [PublicAPI]
    public static class SwaggerApiVersioningExtensions
    {
        public static IServiceCollection AddSwaggerApiVersioning(this IServiceCollection services, Action<string, OpenApiInfo> configureVersionInfo = null)
        {
            return services.AddSingleton<IConfigureOptions<SwaggerGenOptions>, ConfigureSwaggerVersions>(
                provider => new ConfigureSwaggerVersions(
                    provider.GetRequiredService<VersionedControllerProvider>(),
                    provider.GetRequiredService<IOptions<ApiVersioningOptions>>(),
                    configureVersionInfo));
        }

        public static void FilterSchemas(
            this SwaggerGenOptions options,
            Action<OpenApiSchema, SchemaFilterContext> filter)
        {
            options.SchemaFilter<FunctionSchemaFilter>(filter);
        }

        public static void FilterOperations(
            this SwaggerGenOptions options,
            Action<OpenApiOperation, OperationFilterContext> filter)
        {
            options.OperationFilter<FunctionOperationFilter>(filter);
        }

        public static void FilterDocument(
            this SwaggerGenOptions options,
            Action<OpenApiDocument, DocumentFilterContext> filter)
        {
            options.DocumentFilter<FunctionDocumentFilter>(filter);
        }

        private class FunctionSchemaFilter : ISchemaFilter
        {
            public FunctionSchemaFilter(Action<OpenApiSchema, SchemaFilterContext> filter)
            {
                Filter = filter;
            }

            public Action<OpenApiSchema, SchemaFilterContext> Filter { get; }

            public void Apply(OpenApiSchema schema, SchemaFilterContext context)
            {
                Filter(schema, context);
            }
        }


        private class FunctionOperationFilter : IOperationFilter
        {
            public FunctionOperationFilter(Action<OpenApiOperation, OperationFilterContext> filter)
            {
                Filter = filter;
            }

            public Action<OpenApiOperation, OperationFilterContext> Filter { get; }

            public void Apply(OpenApiOperation operation, OperationFilterContext context)
            {
                Filter(operation, context);
            }
        }

        private class FunctionDocumentFilter : IDocumentFilter
        {
            public FunctionDocumentFilter(Action<OpenApiDocument, DocumentFilterContext> filter)
            {
                Filter = filter;
            }

            public Action<OpenApiDocument, DocumentFilterContext> Filter { get; }

            public void Apply(OpenApiDocument model, DocumentFilterContext context)
            {
                Filter(model, context);
            }
        }
    }
}
