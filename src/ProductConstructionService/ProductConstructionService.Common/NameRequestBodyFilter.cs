// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace ProductConstructionService.Common;

internal class NameRequestBodyFilter : IRequestBodyFilter
{
    public void Apply(OpenApiRequestBody requestBody, RequestBodyFilterContext context)
    {
        requestBody.Extensions?["x-name"] = new RequestBodyNameExtension
        {
            Name = context.BodyParameterDescription.Name,
        };
    }
}
