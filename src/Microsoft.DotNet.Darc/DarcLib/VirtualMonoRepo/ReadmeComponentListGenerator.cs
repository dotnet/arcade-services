// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.Helpers;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public interface IReadmeComponentListGenerator
{
    Task UpdateReadme(string templatePath);
}

/// <summary>
/// Class responsible for generating the dynamic list of components in README.md.
/// </summary>
public class ReadmeComponentListGenerator : IReadmeComponentListGenerator
{
    private const string ComponentListStartTag = "<!-- component list beginning -->";
    private const string ComponentListEndTag = "<!-- component list end -->";

    private readonly ISourceManifest _sourceManifest;
    private readonly IVmrInfo _vmrInfo;
    private readonly IFileSystem _fileSystem;

    public ReadmeComponentListGenerator(
        ISourceManifest sourceManifest,
        IVmrInfo vmrInfo,
        IFileSystem fileSystem)
    {
        _sourceManifest = sourceManifest;
        _vmrInfo = vmrInfo;
        _fileSystem = fileSystem;
    }

    public async Task UpdateReadme(string templatePath)
    {
        if (!_fileSystem.FileExists(templatePath))
        {
            return;
        }

        var readmePath = _vmrInfo.VmrPath / VmrInfo.ReadmeFileName;

        using var readStream = _fileSystem.GetFileStream(templatePath, FileMode.Open, FileAccess.Read);
        using var writeStream = _fileSystem.GetFileStream(readmePath, FileMode.Create, FileAccess.Write);
        using var reader = new StreamReader(readStream);
        using var writer = new StreamWriter(writeStream, Encoding.UTF8);
        
        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            await writer.WriteLineAsync(line);

            if (line.Contains(ComponentListStartTag))
            {
                await WriteComponentList(writer);

                while (!line.Contains(ComponentListEndTag))
                {
                    line = reader.ReadLine();

                    if (line == null)
                    {
                        throw new Exception("Component list end tag not found in README.md");
                    }
                }

                await writer.WriteLineAsync(line);
            }
        }
    }

    private async Task WriteComponentList(StreamWriter writer)
    {
        var submodules = _sourceManifest.Submodules.OrderBy(s => s.Path).ToList();

        foreach (ISourceComponent component in _sourceManifest.Repositories.OrderBy(m => m.Path))
        {
            await WriteComponentListItem(writer, component, 0);

            foreach (var submodule in submodules.Where(s => s.Path.StartsWith($"{component.Path}/")))
            {
                await WriteComponentListItem(writer, submodule, 4);
            }
        }
    }

    private static async Task WriteComponentListItem(StreamWriter writer, ISourceComponent component, int indentation)
    {
        // TODO (https://github.com/dotnet/arcade/issues/10549): Add also non-GitHub implementations
        string[] uriParts = component.RemoteUri.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string repo = $"{uriParts[^2]}/{uriParts[^1]}";
        if (repo.EndsWith(".git"))
        {
            repo = repo[..^4];
        }

        await writer.WriteLineAsync($"{new string(' ', indentation)}- `{VmrInfo.RelativeSourcesDir / component.Path}`  ");
        await writer.WriteLineAsync($"{new string(' ', indentation)}*[{repo}@{Commit.GetShortSha(component.CommitSha)}]({component.GetPublicUrl()})*");
    }
}
