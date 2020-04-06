using Microsoft.DotNet.Internal.DependencyInjection.Testing;
using Xunit;

namespace DependencyUpdater.Tests
{
    public class DependencyRegistrationTests
    {
        [Fact]
        public void AreDependenciesRegistered()
        {
            Assert.True(DependencyInjectionValidation.IsDependencyResolutionCoherent(Program.Configure, out string message), message);
        }
    }
}
