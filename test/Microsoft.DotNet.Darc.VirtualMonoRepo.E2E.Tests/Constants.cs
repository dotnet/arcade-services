// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

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

    public static readonly string ProductRepoName = "product-repo1";
    public static readonly string DependencyRepoName = "dependency";
    public static readonly string SecondRepoName = "product-repo2";
    public static readonly string InstallerRepoName = "installer";
    public static readonly string VmrName = "vmr";
    public static readonly string TmpFolderName = "tmp";
    public static readonly string PatchesFolderName = "patches";
    public static string GetRepoFileName(string repoName) => repoName + "-file.txt";
}
