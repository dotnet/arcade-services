// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.ComponentModel.DataAnnotations;

namespace Maestro.Web.Api.v2020_02_20.Models;

public class Channel
{
    public Channel(Data.Models.Channel other)
    {
        ArgumentNullException.ThrowIfNull(other);

        Id = other.Id;
        Name = other.Name;
        Classification = other.Classification;
    }

    public int Id { get; }

    [Required]
    public string Name { get; }

    [Required]
    public string Classification { get; }
}
