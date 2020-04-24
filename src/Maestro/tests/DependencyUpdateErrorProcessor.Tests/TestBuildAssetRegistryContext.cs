// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Maestro.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace DependencyUpdateErrorProcessor.Tests
{
    public class TestBuildAssetRegistryContext : BuildAssetRegistryContext
    {
        public TestBuildAssetRegistryContext(IHostEnvironment hostingEnvironment,
            DbContextOptions<TestBuildAssetRegistryContext> options) : base(
            hostingEnvironment, options)
        {

        }

        public List<RepositoryBranchUpdateHistoryEntry> RepoBranchUpdateInMemory { get; set; }
        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            RepositoryBranchUpdateHistory = RepoBranchUpdateInMemory.AsQueryable();
        }

        public override Task<long> GetInstallationId(string repositoryUrl)
        {
            long result = 2334;
            return Task.FromResult(result);
        }
    }
}
