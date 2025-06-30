// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Threading.Tasks;
using CommandLine;
using Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.DotNet.Darc.Options.VirtualMonoRepo;

[Verb("reset", HelpText = "Resets the contents of a VMR mapping to match a specific commit SHA from the source repository (stages the changes only).")]
internal class ResetCommandLineOptions : VmrCommandLineOptions<ResetOperation>
{
    [Value(0, Required = true, HelpText = 
        "Repository mapping and target SHA in the format [mapping]:[sha]. " +
        "Example: runtime:abc123def456 will reset the runtime mapping to commit abc123def456.")]
    public string Target { get; set; }

    [Option("additional-remotes", Required = false, HelpText =
        "List of additional remote URIs to consider during resetting [mapping name]:[remote URI]. " +
        "Example: installer:https://github.com/myfork/runtime sdk:/local/path/to/sdk")]
    [RedactFromLogging]
    public IEnumerable<string> AdditionalRemotes { get; set; }

    public override IServiceCollection RegisterServices(IServiceCollection services)
    {
        services = base.RegisterServices(services);
        services.AddTransient<IVmrUpdater, VmrReseter>();
        return services;
    }

    private class VmrReseter : VmrUpdater
    {
        public VmrReseter(
            IVmrDependencyTracker dependencyTracker,
            IRepositoryCloneManager cloneManager,
            IVmrPatchHandler patchHandler,
            IThirdPartyNoticesGenerator thirdPartyNoticesGenerator,
            ICodeownersGenerator codeownersGenerator,
            ICredScanSuppressionsGenerator credScanSuppressionsGenerator,
            ILocalGitClient localGitClient,
            ILocalGitRepoFactory localGitRepoFactory,
            IGitRepoFactory gitRepoFactory,
            ILogger<VmrUpdater> logger,
            ISourceManifest sourceManifest,
            IVmrInfo vmrInfo)
            : base(dependencyTracker, cloneManager, patchHandler, thirdPartyNoticesGenerator, codeownersGenerator, credScanSuppressionsGenerator, localGitClient, localGitRepoFactory, gitRepoFactory, logger, sourceManifest, vmrInfo)
        {
        }

        // We don't want to commit files, just stage them
        protected override Task CommitAsync(string commitMessage, (string Name, string Email)? author = null)
            => Task.CompletedTask;
    }
}
