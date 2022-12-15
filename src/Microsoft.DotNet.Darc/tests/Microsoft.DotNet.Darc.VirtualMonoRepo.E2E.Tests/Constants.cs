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

    public static readonly string EmptyVersionDetails = 
@"<?xml version=""1.0"" encoding=""utf-8""?>
<Dependencies>
    <ProductDependencies>
    </ProductDependencies>
    <ToolsetDependencies>
    </ToolsetDependencies>
</Dependencies>";
    
    public static readonly string DependencyTemplate = 
@"<Dependency Name=""{0}"" Version=""8.0.0"">
    <Uri>{1}</Uri>
    <Sha>{2}</Sha>
    <SourceBuild RepoName=""{0}"" ManagedOnly=""true"" />
</Dependency>";

    public static readonly string SourceMappingsTemplate =
        @"{{
  ""patchesPath"": ""{0}"",
  ""additionalMappings"": [
    {1}
  ],

  ""defaults"": {{
      ""defaultRef"": ""main"",
      ""exclude"": [
        ""**/*.dll"",
        ""**/*.Dll"",
        ""**/*.exe"",
        ""**/*.pdb"",
        ""**/*.mdb"",
        ""**/*.zip"",
        ""**/*.nupkg""
      ]
    }},

  ""mappings"": [
    {2}
  ]
}}";

    public static readonly string MappingTemplate =
        @"{{
    ""name"": ""{0}"",
    ""defaultRemote"": ""{1}"",
    ""exclude"": [
      {2}
    ]
}}";

    public static readonly string AdditionalMappingTemplate =
        @"{{
    ""source"": ""{0}"",
    ""destination"": ""{1}""
}}";

    public static readonly string ProductRepoName = "test-repo";
    public static readonly string DependencyRepoName = "dependency";
    public static readonly string SubmoduleRepoName = "external-repo";
    public static readonly string InstallerRepoName = "installer";
    public static readonly string VmrName = "vmr";
    public static readonly string TmpFolderName = "tmp";
    public static readonly string PatchesFolderName = "patches";
    public static readonly string ProductRepoFileName = "test-repo-file.txt";
}
