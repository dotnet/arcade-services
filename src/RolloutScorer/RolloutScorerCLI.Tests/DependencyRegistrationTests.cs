using System;
using FluentAssertions;
using Microsoft.DotNet.Internal.DependencyInjection.Testing;
using NUnit.Framework;

namespace RolloutScorerCLI.Tests
{
    [TestFixture]
    public class DependencyRegistrationTests
    {
        [Test]
        public void AreDependenciesRegistered()
        {
            DependencyInjectionValidation.IsDependencyResolutionCoherent(
                services =>
                {
                    Program.ConfigureServices(services);
                },
                out string message,
                additionalScopedTypes: new[] { typeof(Program) }).Should().BeTrue(message);
        }
    }
}
