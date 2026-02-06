// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using AwesomeAssertions;
using NUnit.Framework;
using ProductConstructionService.BarViz.Code.Helpers;

namespace ProductConstructionService.BarViz.Tests;


[TestFixture]
public class RepoUrlConverterTests
{
    [Test]
    public void ConvertSlugToRepoUrl()
    {
        RepoUrlConverter.SlugToRepoUrl("github:org:repo").Should().Be("https://github.com/org/repo");
        RepoUrlConverter.SlugToRepoUrl("github:dotnet:aspire").Should().Be("https://github.com/dotnet/aspire");
        RepoUrlConverter.SlugToRepoUrl("github:dotnet:runtime").Should().Be("https://github.com/dotnet/runtime");
        RepoUrlConverter.SlugToRepoUrl("azdo:dnceng:internal:dotnet-aspire").Should().Be("https://dev.azure.com/dnceng/internal/_git/dotnet-aspire");
        RepoUrlConverter.SlugToRepoUrl("azdo:dnceng:internal:dotnet-runtime").Should().Be("https://dev.azure.com/dnceng/internal/_git/dotnet-runtime");
        RepoUrlConverter.SlugToRepoUrl("azuredevops:dnceng:internal:dotnet-runtime").Should().Be("https://dev.azure.com/dnceng/internal/_git/dotnet-runtime");
    }

    [Test]
    public void ConvertRepoUrlToSlug()
    {
        RepoUrlConverter.RepoUrlToSlug("https://github.com/org/repo").Should().Be("github:org:repo");
        RepoUrlConverter.RepoUrlToSlug("https://github.com/dotnet/aspire").Should().Be("github:dotnet:aspire");
        RepoUrlConverter.RepoUrlToSlug("https://github.com/dotnet/runtime").Should().Be("github:dotnet:runtime");
        RepoUrlConverter.RepoUrlToSlug("https://dev.azure.com/dnceng/internal/_git/dotnet-aspire").Should().Be("azdo:dnceng:internal:dotnet-aspire");
        RepoUrlConverter.RepoUrlToSlug("https://dev.azure.com/dnceng/internal/_git/dotnet-runtime").Should().Be("azdo:dnceng:internal:dotnet-runtime");
        RepoUrlConverter.RepoUrlToSlug("https://dev.azure.com/dnceng/internal/_git/dotnet-runtime").Should().Be("azdo:dnceng:internal:dotnet-runtime");
        RepoUrlConverter.RepoUrlToSlug("https://dev.azure.com/dnceng/internal/_git/dotnet-wpf-int").Should().Be("azdo:dnceng:internal:dotnet-wpf-int");
    }
}
