// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.OpenApi;
using Microsoft.OpenApi.Interfaces;
using Microsoft.OpenApi.Writers;

namespace ProductConstructionService.Api.Configuration;

internal class RequestBodyNameExtension : IOpenApiExtension
{
    public string? Name { get; set; }

    public void Write(IOpenApiWriter writer, OpenApiSpecVersion specVersion)
    {
        if (Name != null)
        {
            writer.WriteValue(Name);
        }
    }
}
