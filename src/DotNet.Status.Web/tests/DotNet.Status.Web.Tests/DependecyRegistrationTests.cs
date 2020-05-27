<<<<<<< HEAD
<<<<<<< HEAD
using Microsoft.DotNet.Internal.DependencyInjection.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Dotnet.Status.Web;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Hosting;
=======
using System.Collections.Generic;
using Microsoft.DotNet.EntityFrameworkCore.Extensions;
=======
>>>>>>> Adding API for collecting telemetry from Arcade Validation runs; test project for DotNet.Status.Web
using Microsoft.DotNet.Internal.DependencyInjection.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
<<<<<<< HEAD
>>>>>>> Initial commit for new API and test project
=======
using Dotnet.Status.Web;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Hosting;
>>>>>>> Adding API for collecting telemetry from Arcade Validation runs; test project for DotNet.Status.Web

namespace Dotnet.Status.Web.Tests
{
    public class DependencyRegistrationTests
    {
        [Fact]
        public void AreDependenciesRegistered()
        {
            Assert.True(DependencyInjectionValidation.IsDependencyResolutionCoherent(s =>
            {
<<<<<<< HEAD
<<<<<<< HEAD
                s.AddSingleton<IConfiguration>();
                s.AddSingleton<IWebHostEnvironment>();
=======
                s.AddSingleton<IHostEnvironment>(new HostingEnvironment
                {
                    EnvironmentName =
                    Environments.Development
                });
                s.AddBuildAssetRegistry(options =>
                {
                    options.UseInMemoryDatabase("BuildAssetRegistry");
                    options.EnableServiceProviderCaching(false);
                });
>>>>>>> Initial commit for new API and test project
=======
                s.AddSingleton<IConfiguration>();
                s.AddSingleton<IWebHostEnvironment>();
>>>>>>> Adding API for collecting telemetry from Arcade Validation runs; test project for DotNet.Status.Web
            },
                    out string message),
                message);
        }

    }
}
