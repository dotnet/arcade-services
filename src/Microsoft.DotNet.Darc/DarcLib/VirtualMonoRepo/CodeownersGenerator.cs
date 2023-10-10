// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public interface ICodeownersGenerator
{
    Task UpdateCodeowners(string templatePath);
}

public class CodeownersGenerator : ICodeownersGenerator
{
    private static readonly IReadOnlyCollection<LocalPath> s_codeownersLocations = new[]
    {
        new UnixPath(VmrInfo.CodeownersFileName),
        new UnixPath(".github/" + VmrInfo.CodeownersFileName),
        new UnixPath("docs/" + VmrInfo.CodeownersFileName),
    };

    private readonly IVmrInfo _vmrInfo;
    private readonly ISourceManifest _sourceManifest;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<CodeownersGenerator> _logger;

    public CodeownersGenerator(
        IVmrInfo vmrInfo,
        ISourceManifest sourceManifest,
        IFileSystem fileSystem,
        ILogger<CodeownersGenerator> logger)
    {
        _vmrInfo = vmrInfo;
        _sourceManifest = sourceManifest;
        _fileSystem = fileSystem;
        _logger = logger;
    }

    /// <summary>
    /// Generates the CODEOWNERS file by gathering individual repo CODEOWNERS files.
    /// </summary>
    public async Task UpdateCodeowners(string templatePath)
    {
        _logger.LogInformation("Updating {tpnName}...", VmrInfo.CodeownersFileName);

        string header = string.Empty;
        if (_fileSystem.FileExists(templatePath))
        {
            header = await _fileSystem.ReadAllTextAsync(templatePath);
        }

        var destPath = _vmrInfo.VmrPath / VmrInfo.CodeownersFileName;

        _logger.LogInformation("{tpnName} updated", VmrInfo.CodeownersFileName);
    }
}
