// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using CommandLine;
using Microsoft.DotNet.Darc.Operations;
using Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

namespace Microsoft.DotNet.Darc.Options.VirtualMonoRepo;

[Verb("backflow", HelpText = "Flows code changes from the VMR back to target repositories.")]
internal class BackflowCommandLineOptions : VmrCommandLineOptions, IBaseVmrCommandLineOptions
{
    [Option("additional-remotes", Required = false, HelpText =
        "List of additional remote URIs to add to mappings in the format [mapping name]:[remote URI]. " +
        "Example: installer:https://github.com/myfork/installer,sdk:/local/path/to/sdk")]
    public IEnumerable<string> AdditionalRemotes { get; set; }

    [Value(0, Required = true, HelpText = "Repositories to backflow in the form of NAME:PATH with mapping name and local path to the target repository. " +
        "Path can be ommitted when --repository-dirs is supplied.")]
    public IEnumerable<string> Repositories { get; set; }

    [Option("action", Required = false, HelpText = "Backflow action to perform with the target repo. One of <create-patches|apply-patches>. Defaults to create-patches")]
    public string Action { get; set; } = "create-patches";

    [Option("repository-dirs", Required = false, HelpText = "Path to where all repositories are checked out to (directory names must match mapping names). " +
        "Substitutes the need to specify path for every backflown repository")]
    public string RepositoryDirectory { get; set; }

    [Option("discard-patches", Required = false, HelpText = "Delete .patch files created during the sync.")]
    public bool DiscardPatches { get; set; } = false;

    public override Operation GetOperation() => new BackflowOperation(this);
}
