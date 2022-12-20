// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Microsoft.DotNet.Darc.Tests.VirtualMonoRepo;

public class Constants
{
    public static readonly string VersionDetailsTemplate = 
@"<?xml version=""1.0"" encoding=""utf-8""?>
<Dependencies>
    <ProductDependencies>
        {0}
    </ProductDependencies>
    <ToolsetDependencies>
    </ToolsetDependencies>
</Dependencies>";

    public static readonly string EmptyVersionDetails = string.Format(VersionDetailsTemplate, string.Empty);
    
    public static readonly string DependencyTemplate = 
@"<Dependency Name=""{0}"" Version=""8.0.0"">
    <Uri>{1}</Uri>
    <Sha>{2}</Sha>
    <SourceBuild RepoName=""{0}"" ManagedOnly=""true"" />
</Dependency>";

    public static readonly string ProductRepoName = "test-repo";
    public static readonly string DependencyRepoName = "dependency";
    public static readonly string SubmoduleRepoName = "external-repo";
    public static readonly string InstallerRepoName = "installer";
    public static readonly string VmrName = "vmr";
    public static readonly string TmpFolderName = "tmp";
    public static readonly string PatchesFolderName = "patches";
    public static readonly string ProductRepoFileName = "test-repo-file.txt";
    public static readonly string InstallerRepoFileName = "installer-file.txt";
    public static readonly string DependencyRepoFileName = "dependency-file.txt";
    public static string GetRepoFileName(string repoName) => repoName + "-file.txt";
}
