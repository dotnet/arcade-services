// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Shouldly;
using NUnit.Framework;
using ProductConstructionService.BarViz.Code.Helpers;

namespace ProductConstructionService.BarViz.Tests;


[TestFixture]
public class RepoUrlConverterTests
{
    [Test]
    public void ConvertSlugToRepoUrl()
    {
        RepoUrlConverter.SlugToRepoUrl("github:org:repo").ShouldBe("https://github.com/org/repo");
        RepoUrlConverter.SlugToRepoUrl("github:dotnet:aspire").ShouldBe("https://github.com/dotnet/aspire");
        RepoUrlConverter.SlugToRepoUrl("github:dotnet:runtime").ShouldBe("https://github.com/dotnet/runtime");
        RepoUrlConverter.SlugToRepoUrl("azdo:dnceng:internal:dotnet-aspire").ShouldBe("https://dev.azure.com/dnceng/internal/_git/dotnet-aspire");
        RepoUrlConverter.SlugToRepoUrl("azdo:dnceng:internal:dotnet-runtime").ShouldBe("https://dev.azure.com/dnceng/internal/_git/dotnet-runtime");
        RepoUrlConverter.SlugToRepoUrl("azuredevops:dnceng:internal:dotnet-runtime").ShouldBe("https://dev.azure.com/dnceng/internal/_git/dotnet-runtime");
    }

    [Test]
    public void ConvertRepoUrlToSlug()
    {
        RepoUrlConverter.RepoUrlToSlug("https://github.com/org/repo").ShouldBe("github:org:repo");
        RepoUrlConverter.RepoUrlToSlug("https://github.com/dotnet/aspire").ShouldBe("github:dotnet:aspire");
        RepoUrlConverter.RepoUrlToSlug("https://github.com/dotnet/runtime").ShouldBe("github:dotnet:runtime");
        RepoUrlConverter.RepoUrlToSlug("https://dev.azure.com/dnceng/internal/_git/dotnet-aspire").ShouldBe("azdo:dnceng:internal:dotnet-aspire");
        RepoUrlConverter.RepoUrlToSlug("https://dev.azure.com/dnceng/internal/_git/dotnet-runtime").ShouldBe("azdo:dnceng:internal:dotnet-runtime");
        RepoUrlConverter.RepoUrlToSlug("https://dev.azure.com/dnceng/internal/_git/dotnet-runtime").ShouldBe("azdo:dnceng:internal:dotnet-runtime");
        RepoUrlConverter.RepoUrlToSlug("https://dev.azure.com/dnceng/internal/_git/dotnet-wpf-int").ShouldBe("azdo:dnceng:internal:dotnet-wpf-int");
    }
}
