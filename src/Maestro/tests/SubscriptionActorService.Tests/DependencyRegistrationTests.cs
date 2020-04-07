using Microsoft.DotNet.Internal.DependencyInjection.Testing;
using Xunit;

namespace SubscriptionActorService.Tests
{
    public class DependencyRegistrationTests
    {
        [Fact]
        public void AreDependenciesRegistered()
        {
            Assert.True(DependencyInjectionValidation.IsDependencyResolutionCoherent(Program.Configure, true, out string message), message);
        }
    }
}
