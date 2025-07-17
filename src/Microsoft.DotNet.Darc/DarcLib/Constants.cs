// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.DotNet.DarcLib;

public class Constants
{
    public const string GitHubBotUserName = "dn-bot";
    public const string DarcBotName = "dotnet-maestro[bot]";
    public const string DarcBotEmail = "dotnet-maestro[bot]@users.noreply.github.com";

    // Well known ID of an empty commit (can be used as a "commit zero" when diffing)
    public const string EmptyGitObject = "4b825dc642cb6eb9a060e54bf8d69288fbee4904";
    public const string HEAD = "HEAD";

    // String used to mark the commit as automated
    public const string AUTOMATION_COMMIT_TAG = "[[ commit created by automation ]]";

    // Character we use in the commit messages to indicate the change
    public const string Arrow = " → ";

    public const string GitHubUrlPrefix = "https://github.com/";
    public const string AzureDevOpsUrlPrefix = "https://dev.azure.com/";

    public const string DefaultVmrUri = "https://github.com/dotnet/dotnet/";

    public static readonly LibGit2Sharp.Identity DotnetBotIdentity = new(DarcBotName, DarcBotEmail);

    public const string EngFolderName = "eng";
    public const string CommonScriptFilesPath = $"{EngFolderName}/common";
    public const string DefaultCommitAuthor = "dotnet-maestro[bot]";
}
