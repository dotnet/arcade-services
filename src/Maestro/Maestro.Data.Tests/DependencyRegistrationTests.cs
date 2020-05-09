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

namespace Maestro.Data.Tests
{
    public class DependencyRegistrationTests
    {
        [Fact]
        public void AreDependenciesRegistered()
        {
            Assert.True(DependencyInjectionValidation.IsDependencyResolutionCoherent(s =>
                    {
                        s.AddSingleton<IHostEnvironment>(new HostingEnvironment{EnvironmentName = 
                            Environments.Development});
                        s.AddBuildAssetRegistry(options => { options.UseInMemoryDatabase("BuildAssetRegistry"); });
                    },
                    out string message),
                message);
        }

    }
}
