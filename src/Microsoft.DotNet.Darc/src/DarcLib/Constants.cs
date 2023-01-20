// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Microsoft.DotNet.DarcLib;

public class Constants
{
    public const string DarcBotName = "dotnet-maestro[bot]";
    public const string DarcBotEmail = "dotnet-maestro[bot]@users.noreply.github.com";

    // Well known ID of an empty commit (can be used as a "commit zero" when diffing)
    public const string EmptyGitObject = "4b825dc642cb6eb9a060e54bf8d69288fbee4904";

    public const string GitHubUrlPrefix = "https://github.com/";
    public const string AzureDevOpsUrlPrefix = "https://dev.azure.com/";
}
