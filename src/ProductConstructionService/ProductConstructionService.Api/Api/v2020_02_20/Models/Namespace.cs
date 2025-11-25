// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace ProductConstructionService.Api.Api.v2020_02_20.Models;

public class Namespace
{
    public Namespace(Maestro.Data.Models.Namespace other)
    {
        ArgumentNullException.ThrowIfNull(other);
        Id = other.Id;
        Name = other.Name;
    }

    public int Id { get; }
    public string Name { get; }
}
