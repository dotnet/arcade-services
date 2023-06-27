// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel.DataAnnotations;
using FluentAssertions;
using Microsoft.DotNet.Internal.Testing.Utility;
using NUnit.Framework;

namespace Maestro.Web.Tests;

[TestFixture]
public class RepositoryUrlAttributeTests
{
    [TestCase("https://github.com/org/validRepo")]
    [TestCase("https://github.com/org/valid.Repo")]
    [TestCase("https://github.com/org/valid-Repo")]
    [TestCase("https://github.com/org/valid-Rep.o12")]
    [TestCase("https://dev.azure.com/org/project/_git/validRepo")]
    [TestCase("https://dev.azure.com/org/project/_git/valid.Repo")]
    [TestCase("https://dev.azure.com/org/project/_git/valid-Repo")]
    [TestCase("https://dev.azure.com/org/project/_git/valid-Rep.o12")]
    public void IsValidWithValidUrl(string url)
    {
        var attrib = new RepositoryUrlAttribute();
        attrib.GetValidationResult(url, new ValidationContext(url)).Should().Be(ValidationResult.Success);
    }

    [TestCase("https://github.com/org/validRepo$")]
    [TestCase("https://github.com/org/valid#Repo")]
    [TestCase("https://github.com/org/valid*Repo")]
    [TestCase("https://github.com/org/valid(Rep)o")]
    [TestCase("https://github.com/validRepo")]
    [TestCase("https://dev.azure.com/org/project/_git")]
    [TestCase("https://dev.azure.com/org/_git/validRepo")]
    [TestCase("https://dev.azure.com/_git/validRepo")]
    [TestCase("https://dev.azure.com/org/project/validRepo")]
    public void IsValidWithInvalidValidUrl(string url)
    {
        var attrib = new RepositoryUrlAttribute();
        attrib.GetValidationResult(url, new ValidationContext(url)).Should().NotBe(ValidationResult.Success);
    }
}
