// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Linq;
using System.Text;
using Microsoft.DotNet.Darc.Models.VirtualMonoRepo;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public interface IReadmeComponentListGenerator
{
    void UpdateReadme();
}

/// <summary>
/// Class responsible for generating the dynamic list of components in README.md.
/// </summary>
public class ReadmeComponentListGenerator : IReadmeComponentListGenerator
{
    private const string ComponentListStartTag = "<!-- component list beginning -->";
    private const string ComponentListEndTag = "<!-- component list end -->";

    private readonly IVmrDependencyTracker _dependencyTracker;
    private readonly IVmrInfo _vmrInfo;

    public ReadmeComponentListGenerator(IVmrDependencyTracker dependencyTracker, IVmrInfo vmrInfo)
    {
        _dependencyTracker = dependencyTracker;
        _vmrInfo = vmrInfo;
    }

    public void UpdateReadme()
    {
        GetComponentList();
    }

    private string GetComponentList()
    {
        var writer = new StringBuilder();
        var submodules = _dependencyTracker.Submodules.OrderBy(s => s.Path).ToList();

        foreach (ISourceComponent component in _dependencyTracker.Sources.OrderBy(m => m.Path))
        {
            WriteComponentListItem(writer, component, 0);

            foreach (var submodule in submodules.Where(s => s.Path.StartsWith($"{component.Path}/")))
            {
                WriteComponentListItem(writer, submodule, 4);
            }
        }

        return writer.ToString();
    }

    private static void WriteComponentListItem(StringBuilder writer, ISourceComponent component, int indentation)
    {
        // TODO (https://github.com/dotnet/arcade/issues/10549): Add also non-GitHub implementations
        string[] uriParts = component.RemoteUri.Split('/', StringSplitOptions.RemoveEmptyEntries);

        writer
            .AppendLine($"{new string(' ', indentation)}- `{VmrInfo.SourcesDir}/{component.Path}`  ")
            .AppendLine($"{new string(' ', indentation)}*[{uriParts[^2]}/{uriParts[^1]}@{Commit.GetShortSha(component.CommitSha)}]({component.GetPublicUrl()})*");
    }
}

/*
    - `src/arcade`  
    *[dotnet/arcade@d149051](https://github.com/dotnet/arcade/commit/d149051ba716ea81b818d69d3d6a944576ad275b)*
    - `src/aspnetcore`  
    *[dotnet/aspnetcore@ce60f0d](https://github.com/dotnet/aspnetcore/commit/ce60f0d10c0adb79b2b824f5da05e41e1199f6cf)*
        - `src/aspnetcore/src/submodules/MessagePack-CSharp`  
        *[aspnet/MessagePack-CSharp@fe9fa08](https://github.com/aspnet/MessagePack-CSharp/tree/fe9fa0834d18492eb229ff2923024af2c87553f8)*
        - `src/aspnetcore/src/submodules/googletest`  
        *[google/googletest@93f08be]
*/
