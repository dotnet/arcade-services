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
using Microsoft.DotNet.Internal.DependencyInjection.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Xunit;
>>>>>>> Initial commit for new API and test project

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
            },
                    out string message),
                message);
        }

    }
}
