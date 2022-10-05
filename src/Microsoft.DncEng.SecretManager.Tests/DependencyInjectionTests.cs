using System.Linq;
using System.Reflection;
using FluentAssertions;
using Microsoft.DncEng.CommandLineLib;
using Microsoft.DotNet.Internal.DependencyInjection.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Microsoft.DncEng.SecretManager.Tests;

public class DependencyInjectionTests
{
    [Test]
    public void AreDependenciesCoherent()
    {
        bool dependenciesCoherent = DependencyInjectionValidation.IsDependencyResolutionCoherent(
            collection =>
            {
                var program = new Program();
                program.ConfigureServiceCollection(collection);
                collection.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
            },
            out string errorMessage,
            typeof(Program).Assembly.GetTypes().Where(t => t.GetCustomAttribute<CommandAttribute>() != null)
        );

        dependenciesCoherent.Should().BeTrue(errorMessage);
    }
}
