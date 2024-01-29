// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.DotNet.Darc.Tests.VirtualMonoRepo;

public class Constants
{
    public const string VersionDetailsTemplate = """
        <?xml version="1.0" encoding="utf-8"?>
        <Dependencies>
            <ProductDependencies>
                {0}
            </ProductDependencies>
            <ToolsetDependencies>
            </ToolsetDependencies>
        </Dependencies>
        """;

    public const string DependencyTemplate = """
        <Dependency Name="{0}" Version="8.0.0">
            <Uri>{1}</Uri>
            <Sha>{2}</Sha>
            <SourceBuild RepoName="{0}" ManagedOnly="true" />
        </Dependency>
        """;

    public const string VersionPropsTemplate = """
        <?xml version="1.0" encoding="utf-8"?>
        <Project>
          <PropertyGroup>
            {0}
          </PropertyGroup>
        </Project>
        """;

    public const string ProductRepoName = "product-repo1";
    public const string DependencyRepoName = "dependency";
    public const string SecondRepoName = "product-repo2";
    public const string InstallerRepoName = "installer";
    public const string VmrName = "vmr";
    public const string TmpFolderName = "tmp";
    public const string PatchesFolderName = "patches";

    public static string GetRepoFileName(string repoName) => repoName + "-file.txt";
}
