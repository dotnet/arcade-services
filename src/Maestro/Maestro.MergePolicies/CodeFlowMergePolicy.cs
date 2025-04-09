// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;

namespace Maestro.MergePolicies;
internal abstract class CodeFlowMergePolicy : MergePolicy
{
    public override string DisplayName => "Code flow verification";

    protected static readonly string configurationErrorsHeader = """
         ### :x: Check Failed

         The following error(s) were encountered:


        """;

    protected static readonly string SeekHelpMsg = $"""


        ### :exclamation: IMPORTANT

        The `{VmrInfo.DefaultRelativeSourceManifestPath}` and `{VersionFiles.VersionDetailsXml}` files are managed by Maestro/darc. Outside of exceptional circumstances, these files should not be modified manually.
        **Unless you are sure that you know what you are doing, we recommend reaching out for help**. You can receive assistance by:
        - tagging the **@dotnet/product-construction** team in a PR comment
        - using the [First Responder channel](https://teams.microsoft.com/l/channel/19%3Aafba3d1545dd45d7b79f34c1821f6055%40thread.skype/First%20Responders?groupId=4d73664c-9f2f-450d-82a5-c2f02756606dhttps://teams.microsoft.com/l/channel/19%3Aafba3d1545dd45d7b79f34c1821f6055%40thread.skype/First%20Responders?groupId=4d73664c-9f2f-450d-82a5-c2f02756606d),
        - [opening an issue](https://github.com/dotnet/arcade-services/issues/new?template=BLANK_ISSUE) in the dotnet/arcade-services repo
        - contacting the [.NET Product Construction Services team via e-mail](mailto:dotnetprodconsvcs@microsoft.com).
        """;
}
