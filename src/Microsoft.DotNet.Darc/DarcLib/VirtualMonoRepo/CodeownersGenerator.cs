// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public interface ICodeownersGenerator
{
    Task UpdateCodeowners(CancellationToken cancellationToken);
}

public class CodeownersGenerator : ICodeownersGenerator
{
    private const string CodeownersHeader = "### CONTENT BELOW IS AUTO-GENERATED AND MANUAL CHANGES WILL BE OVERWRITTEN ###";

    private static readonly IReadOnlyCollection<LocalPath> s_codeownersLocations = new[]
    {
        new UnixPath(VmrInfo.CodeownersFileName),
        new UnixPath(".github/" + VmrInfo.CodeownersFileName),
        new UnixPath("docs/" + VmrInfo.CodeownersFileName),
    };

    private readonly IVmrInfo _vmrInfo;
    private readonly ISourceManifest _sourceManifest;
    private readonly ILocalGitRepo _localGitClient;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<CodeownersGenerator> _logger;

    public CodeownersGenerator(
        IVmrInfo vmrInfo,
        ISourceManifest sourceManifest,
        ILocalGitRepo localGitClient,
        IFileSystem fileSystem,
        ILogger<CodeownersGenerator> logger)
    {
        _vmrInfo = vmrInfo;
        _sourceManifest = sourceManifest;
        _localGitClient = localGitClient;
        _fileSystem = fileSystem;
        _logger = logger;
    }

    /// <summary>
    /// Generates the CODEOWNERS file by gathering individual repo CODEOWNERS files.
    /// </summary>
    public async Task UpdateCodeowners(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Updating {tpnName}...", VmrInfo.CodeownersFileName);

        var destPath = _vmrInfo.VmrPath / VmrInfo.CodeownersFileName;

        string header = CodeownersHeader;
        if (_fileSystem.FileExists(destPath))
        {
            var content = await _fileSystem.ReadAllTextAsync(destPath);

            int position = content.IndexOf(CodeownersHeader);
            if (position != -1)
            {
                header = content.Substring(0, position + CodeownersHeader.Length);
            }
        }

        using (var destStream = _fileSystem.GetFileStream(destPath, FileMode.Create, FileAccess.Write))
        using (var writer = new StreamWriter(destStream, Encoding.UTF8))
        {
            if (!string.IsNullOrEmpty(header))
            {
                await writer.WriteAsync(header);
            }

            foreach (ISourceComponent component in _sourceManifest.Repositories.OrderBy(m => m.Path))
            {
                await AddCodeownersContent(component.Path, writer, cancellationToken);

                foreach (var submodule in _sourceManifest.Submodules.Where(s => s.Path.StartsWith($"{component.Path}/")))
                {
                    await AddCodeownersContent(submodule.Path, writer, cancellationToken);
                }
            }
        }

        await _localGitClient.StageAsync(_vmrInfo.VmrPath, new[] { VmrInfo.CodeownersFileName }, cancellationToken);

        _logger.LogInformation("{tpnName} updated", VmrInfo.CodeownersFileName);
    }

    private async Task AddCodeownersContent(string repoPath, StreamWriter writer, CancellationToken cancellationToken)
    {
        // CODEOWNERS files are very restricted in size so we can safely work with them in-memory
        var content = new List<string>();

        foreach (var location in s_codeownersLocations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var codeownersPath = _vmrInfo.VmrPath / VmrInfo.SourcesDir / repoPath / location;

            if (_fileSystem.FileExists(codeownersPath))
            {
                var codeownersContent = await _fileSystem.ReadAllTextAsync(codeownersPath);
                content.AddRange(codeownersContent.Split('\n'));
                content.Add(string.Empty);
            }
        }

        if (content.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await writer.WriteAsync("\n\n");
            await writer.WriteAsync($"## {(repoPath + ' ').PadRight(CodeownersHeader.Length - 3, '#')}\n\n");
            await writer.WriteAsync(string.Join('\n', content.Select(rule => FixCodeownerRule(repoPath, rule))));
        }
    }

    /// <summary>
    /// Fixes a codeowners rule by prefixing the path with the VMR location.
    /// </summary>
    private static string FixCodeownerRule(string repoPath, string line)
    {
        if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
        {
            return line;
        }

        return $"/{VmrInfo.SourcesDir}/{repoPath}{(line.StartsWith('/') ? string.Empty : '/')}{line}";
    }
}
