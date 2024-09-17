using FluentAssertions;
using NUnit.Framework;
using ProductConstructionService.BarViz.Code.Services;

namespace ProductConstructionService.BarViz.Tests;


[TestFixture]
public class RedirectsTests
{
    [Test]
    public void RedirectFromOldUrlType()
    {
        var urlRedirectManager = new UrlRedirectManager();

        urlRedirectManager.ApplyLocatinoRedirects("http://localhost:11/3883/https:%2F%2Fgithub.com%2Fdotnet%2Faspire/latest/graph")
            .Should().Be("/channel-3883/github:dotnet:aspire/build-latest");

        urlRedirectManager.ApplyLocatinoRedirects("http://localhost/5172/https%3A%2F%2Fdev.azure.com%2Fdnceng%2Finternal%2F_git%2Fdotnet-wpf-int/latest/graph")
            .Should().Be("/channel-5172/azdo:dnceng:internal:dotnet-wpf-int/build-latest");
    }
}
