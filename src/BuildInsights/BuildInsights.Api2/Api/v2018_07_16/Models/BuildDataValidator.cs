// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using FluentValidation;

#nullable disable
namespace ProductConstructionService.Api.v2018_07_16.Models;

public class BuildDataValidator : AbstractValidator<BuildData>
{
    public BuildDataValidator()
    {
        RuleFor(b => b.Assets).NotEmpty();
    }
}
