// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.OpenApi.Interfaces;
using Microsoft.OpenApi.Writers;
using Microsoft.OpenApi;

namespace ProductConstructionService.Common;

internal class PaginatedExtension : IOpenApiExtension
{
    public string? PageParameterName { get; set; }
    public string? PageSizeParameterName { get; set; }

    public void Write(IOpenApiWriter writer, OpenApiSpecVersion specVersion)
    {
        writer.WriteStartObject();
        writer.WriteProperty("page", PageParameterName);
        writer.WriteProperty("pageSize", PageSizeParameterName);
        writer.WriteEndObject();
    }
}
