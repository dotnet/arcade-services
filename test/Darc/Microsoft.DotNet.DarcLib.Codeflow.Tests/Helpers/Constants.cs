// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.DotNet.DarcLib.Codeflow.Tests;

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
          </PropertyGroup>
        </Project>

        """;

    public const string GlobalJsonTemplate = """
        {
          "tools": {
            "dotnet": "9.0.100"
          }
        }
        """;
    public const string VmrBaseDotnetSdkVersion = "9.0.101";
    public const string VmrBaseGlobalJsonTemplate = """
        {
          "tools": {
            "dotnet": "9.0.101"
          }
        }
        """;

    public const string NuGetConfigTemplate = """
        <?xml version="1.0" encoding="utf-8"?>
        <configuration>
          <packageSources>
            <clear />
            <add key="dotnet-eng" value="https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet-eng/nuget/v3/index.json" />
            <add key="dotnet-tools" value="https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet-tools/nuget/v3/index.json" />
            <add key="dotnet-public" value="https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet-public/nuget/v3/index.json" />
          </packageSources>
          <disabledPackageSources />
        </configuration>

        """;

    public const string ProductRepoName = "product-repo1";
    public const string DependencyRepoName = "dependency";
    public const string SecondRepoName = "product-repo2";
    public const string SyncDisabledRepoName = "sync-disabled-repo";
    public const string InstallerRepoName = "installer";
    public const string VmrName = "vmr";
    public const string TmpFolderName = "tmp";
    public const string PatchesFolderName = "patches";

    public static string GetRepoFileName(string repoName) => repoName + "-file.txt";
}
