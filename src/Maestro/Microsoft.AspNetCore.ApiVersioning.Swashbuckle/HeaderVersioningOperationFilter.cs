// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using JetBrains.Annotations;
using Microsoft.AspNetCore.ApiVersioning.Schemes;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.Swagger;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Microsoft.AspNetCore.ApiVersioning.Swashbuckle
{
    [UsedImplicitly(ImplicitUseKindFlags.InstantiatedWithFixedConstructorSignature)]
    internal class HeaderVersioningOperationFilter : IOperationFilter
    {
        private readonly HeaderVersioningScheme _scheme;

        public HeaderVersioningOperationFilter(HeaderVersioningScheme scheme)
        {
            _scheme = scheme;
        }

        public void Apply(OpenApiOperation operation, OperationFilterContext context)
        {
            string version = context.ApiDescription.ActionDescriptor.RouteValues["version"];
            if (operation.Parameters == null)
            {
                operation.Parameters = new List<OpenApiParameter>();
            }

            operation.Parameters.Add(
                new OpenApiParameter
                {
                    In = ParameterLocation.Header,
                    Name = _scheme.HeaderName,
                    Description = "The api version",
                    Required = true,
                    Schema = new OpenApiSchema
                    {
                        Type = "string",
                        Enum = new List<IOpenApiAny>
                        {
                            new OpenApiString(version)
                        },
                    },
                });
        }
    }
}
