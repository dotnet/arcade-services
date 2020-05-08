using System;
using System.IO;
using Maestro.Data;
using Microsoft.DotNet.ServiceFabric.ServiceHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace EfTest
{
    class Program
    {
        static void Main(string[] args)
        {
            ServiceCollection services = new ServiceCollection();
            services.AddBuildAssetRegistry(
                options =>
                {
                    options.UseSqlServer("Data Source=localhost\\SQLEXPRESS;Initial Catalog=BuildAssetRegistry;Integrated Security=true");
                });
            services.AddSingleton<IHostEnvironment>(new HostEnvironment(Environments.Development, "EFTest", Path.GetFullPath("."), null));

            var provider = services.BuildServiceProvider();
            var ctx = provider.GetRequiredService<BuildAssetRegistryContext>();
            using var txn = ctx.Database.BeginTransaction();
        }
    }
}
