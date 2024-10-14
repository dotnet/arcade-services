// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using CommandLine;
using Microsoft.DotNet.Darc.Operations;

namespace Microsoft.DotNet.Darc.Options.VirtualMonoRepo;

internal interface ICodeFlowCommandLineOptions : IBaseVmrCommandLineOptions
{
    int? Build { get; set; }
    string Commit { get; set; }
    bool DiscardPatches { get; set; }
    string RepositoryDirectory { get; set; }
    public string BaseBranch { get; set; }
    public string TargetBranch { get; set; }
}

internal abstract class CodeFlowCommandLineOptions<T> : VmrCommandLineOptions<T>, IBaseVmrCommandLineOptions, ICodeFlowCommandLineOptions where T : Operation
{
    public abstract IEnumerable<string> Repositories { get; set; }

    [Option("additional-remotes", Required = false, HelpText =
        "List of additional remote URIs to add to mappings in the format [mapping name]:[remote URI]. " +
        "Example: installer:https://github.com/myfork/installer,sdk:/local/path/to/sdk")]
    [RedactFromLogging]
    public IEnumerable<string> AdditionalRemotes { get; set; }

    [Option("repository-dirs", Required = false, HelpText = "Path to where all repositories are checked out to (directory names must match mapping names). " +
        "Substitutes the need to specify path for every backflown repository.")]
    public string RepositoryDirectory { get; set; }

    [Option("discard-patches", Required = false, HelpText = "Delete .patch files created during the sync.")]
    public bool DiscardPatches { get; set; } = false;

    [Option("build", Required = false, HelpText = "If specified, flows the given build. Cannot be used with --ref.")]
    public int? Build { get; set; }

    [Option("commit", Required = false, HelpText = "If specified, flows the given commit. Cannot be used with --build.")]
    public string Commit { get; set; }

    [Option("base-branch", Required = false, HelpText = "Name of the branch of the target repository to apply changes on top of. Defaults to checked out branch")]
    public string BaseBranch { get; set; }

    [Option("target-branch", Required = false, HelpText = "Name of the new branch that will be created in the target repository. Defaults to codeflow/SHA1-SHA2")]
    public string TargetBranch { get; set; }
}
