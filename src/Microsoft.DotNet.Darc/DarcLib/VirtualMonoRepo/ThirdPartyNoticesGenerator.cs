// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public interface IThirdPartyNoticesGenerator
{
    Task UpdateThirdPartyNotices(string templatePath);
}

public class ThirdPartyNoticesGenerator : IThirdPartyNoticesGenerator
{
    private static readonly Regex TpnFileName = new(@"third-?party-?notices(.txt)?$", RegexOptions.IgnoreCase);

    private readonly IVmrInfo _vmrInfo;
    private readonly IVmrDependencyTracker _dependencyTracker;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<ThirdPartyNoticesGenerator> _logger;

    public ThirdPartyNoticesGenerator(
        IVmrInfo vmrInfo,
        IVmrDependencyTracker dependencyTracker,
        IFileSystem fileSystem,
        ILogger<ThirdPartyNoticesGenerator> logger)
    {
        _vmrInfo = vmrInfo;
        _dependencyTracker = dependencyTracker;
        _fileSystem = fileSystem;
        _logger = logger;
    }

    /// <summary>
    /// Generates the THIRD-PARTY-NOTICES.txt file by assembling other similar files from the whole VMR.
    /// </summary>
    public async Task UpdateThirdPartyNotices(string templatePath)
    {
        _logger.LogInformation("Updating {tpnName}...", VmrInfo.ThirdPartyNoticesFileName);

        string header = string.Empty;
        if (_fileSystem.FileExists(templatePath))
        {
            header = await _fileSystem.ReadAllTextAsync(templatePath);
        }

        var vmrTpnPath = _vmrInfo.VmrPath / VmrInfo.ThirdPartyNoticesFileName;

        using (var tpnWriter = new StreamWriter(vmrTpnPath, append: false))
        {
            await tpnWriter.WriteAsync(header);

            foreach (var notice in GetAllNotices())
            {
                _logger.LogDebug("Processing {name}...", notice);

                var repo = _fileSystem.GetFileName(_fileSystem.GetDirectoryName(notice));

                tpnWriter.WriteLine(new string('#', 45));
                tpnWriter.WriteLine($"### {repo}");
                tpnWriter.WriteLine(new string('#', 45));
                tpnWriter.WriteLine();

                tpnWriter.WriteLine(await _fileSystem.ReadAllTextAsync(notice));
                tpnWriter.WriteLine();
            }
        }

        _logger.LogInformation("{tpnName} updated", VmrInfo.ThirdPartyNoticesFileName);
    }

    private IEnumerable<string> GetAllNotices()
    {
        var paths = new List<string>();

        foreach (var possiblePath in _dependencyTracker.Mappings.Select(_vmrInfo.GetRepoSourcesPath))
        {
            if (!_fileSystem.DirectoryExists(possiblePath))
            {
                continue;
            }

            foreach (var tpn in _fileSystem.GetFiles(possiblePath).Where(IsTpnPath))
            {
                paths.Add(tpn);
            }
        }

        return paths.OrderBy(p => p);
    }

    /// <summary>
    /// Checkes if a given path is a THIRD-PARTY-NOTICES.txt file.
    /// </summary>
    public static bool IsTpnPath(string path) => TpnFileName.IsMatch(path);
}
