using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.DotNet.Internal.Testing.DependencyInjection.Abstractions;
using Microsoft.DotNet.Internal.Testing.Utility;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace Microsoft.DotNet.GitHub.Authentication.Tests;

public partial class GitHubAppTokenProviderTests
{
    [TestDependencyInjectionSetup]
    private static class TestDataSetup
    {
        public static void Default(IServiceCollection services)
        {
            services.AddOptions();
            services.AddLogging(l => l.AddProvider(new NUnitLogger()));
            services.AddSingleton<ISystemClock, TestClock>();
        }

        public static Func<IServiceProvider,IGitHubAppTokenProvider> Provider(IServiceCollection service, string keyPem)
        {
            service.Configure<GitHubTokenProviderOptions>(o =>
            {
                o.GitHubAppId = 999;
                o.PrivateKey = keyPem;
            });
            service.AddSingleton<IGitHubAppTokenProvider, GitHubAppTokenProvider>();

            return s => s.GetRequiredService<IGitHubAppTokenProvider>();
        }
    }

    [Test]
    public void AppTokenReturnsValue()
    {
        // Just use a random RSA key, since we just want to see if it can produce... something
        // There isn't a great way to use a "test" key, so random is what we'll use
        using var value = RSA.Create(4096); // lgtm [cs/cryptography/default-rsa-key-construction] False positive. This does not use the default constructor.
        string pem = ExportToPem(value);
        TestData testData = TestData
            .Default
            .WithKeyPem(pem)
            .Build();
        IGitHubAppTokenProvider provider = testData.Provider;
        string token = provider.GetAppToken();
        token.Should().Contain(".", because:"valid JWT are in the format XXX.YYY.ZZZ");
    }

        
    // This, unfortunately, isn't something the framework can do for us, but luckily it's an easy thing to do ourselves
    private static string ExportToPem(RSA key)
    {
        var buffer = new StringBuilder();

        buffer.AppendLine("-----BEGIN RSA PRIVATE KEY-----");
        buffer.AppendLine(
            Convert.ToBase64String(
                key.ExportRSAPrivateKey(),
                Base64FormattingOptions.InsertLineBreaks
            )
        );
        buffer.AppendLine("-----END RSA PRIVATE KEY-----");

        return buffer.ToString();
    }
}
