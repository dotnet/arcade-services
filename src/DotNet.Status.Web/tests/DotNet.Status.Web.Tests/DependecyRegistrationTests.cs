using Microsoft.DotNet.Internal.DependencyInjection.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Dotnet.Status.Web;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Hosting;

namespace Dotnet.Status.Web.Tests
{
    public class DependencyRegistrationTests
    {
        [Fact]
        public void AreDependenciesRegistered()
        {
            Assert.True(DependencyInjectionValidation.IsDependencyResolutionCoherent(s =>
            {
                s.AddSingleton<IConfiguration>();
                s.AddSingleton<IWebHostEnvironment>();
            },
                    out string message),
                message);
        }

    }
}
