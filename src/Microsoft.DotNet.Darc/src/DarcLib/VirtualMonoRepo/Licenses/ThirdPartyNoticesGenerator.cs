// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo.Licenses;

public interface IThirdPartyNoticesGenerator
{
    Task UpdateThirtPartyNotices();
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
    /// <param name="force">Force generation (skip check of notice changes)</param>
    public async Task UpdateThirtPartyNotices()
    {
        _logger.LogInformation("Updating {tpnName}...", VmrInfo.ThirdPartyNoticesFileName);

        var vmrTpnPath = _fileSystem.PathCombine(_vmrInfo.VmrPath, VmrInfo.ThirdPartyNoticesFileName);
        var vmrTpn = _fileSystem.FileExists(vmrTpnPath)
            ? TpnDocument.Parse((await _fileSystem.ReadAllTextAsync(vmrTpnPath)).Replace("\r\n", "\n").Split('\n'))
            : new TpnDocument(string.Empty, Array.Empty<TpnSection>());

        // TODO: Remove?
        _logger.LogDebug("Current sections:");
        foreach (var s in vmrTpn.Sections.OrderBy(s => s.Header.SingleLineName))
        {
            _logger.LogDebug("  {section}", $"{s.Header.StartLine + 1}:{s.Header.StartLine + s.Header.LineLength} {s.Header.Format} '{s.Header.SingleLineName}'");
        }

        var tpns = new List<TpnDocument>();

        foreach (var notice in GetAllNotices())
        {
            _logger.LogDebug("Processing {name}...", notice);

            var content = await _fileSystem.ReadAllTextAsync(notice);
            tpns.Add(TpnDocument.Parse(content.Replace("\r\n", "\n").Split('\n')));
        }

        TpnSection[] newSections = tpns
            .SelectMany(o => o.Sections)
            .Except(vmrTpn.Sections, TpnSection.SectionComparer)
            .OrderBy(s => s.Header.Name)
            .ToArray();

        foreach (TpnSection existing in tpns
            .SelectMany(r => r.Sections.Except(newSections))
            .Where(s => !newSections.Contains(s))
            .OrderBy(s => s.Header.Name))
        {
            _logger.LogDebug("Found already-imported section: '{sectionName}'", existing.Header.SingleLineName);
        }

        foreach (var s in newSections)
        {
            _logger.LogDebug("New section to import: '{sectionName}', line {line}", s.Header.SingleLineName, s.Header.StartLine);
        }

        _logger.LogDebug("Importing {count} sections...", newSections.Length);

        var newTpn = new TpnDocument(vmrTpn.Preamble, vmrTpn.Sections.Concat(newSections).ToList());
        _fileSystem.WriteToFile(vmrTpnPath, newTpn.ToString());

        _logger.LogInformation("{tpnName} updated", VmrInfo.ThirdPartyNoticesFileName);
    }

    private IEnumerable<string> GetAllNotices()
    {
        var repoPaths = _dependencyTracker.Mappings
            .Select(_vmrInfo.GetRepoSourcesPath);

        var submodulePaths = _dependencyTracker.GetSubmodules()
            .Select(s => _fileSystem.PathCombine(
                _vmrInfo.VmrPath,
                VmrInfo.SourcesDir,
                s.Path.Replace('/', _fileSystem.DirectorySeparatorChar)));

        foreach (var possiblePath in repoPaths.Concat(submodulePaths))
        {
            if (!_fileSystem.DirectoryExists(possiblePath))
            {
                continue;
            }

            foreach (var tpn in _fileSystem.GetFiles(possiblePath).Where(IsTpnPath))
            {
                yield return tpn;
            }
        }
    }

    /// <summary>
    /// Checkes if a given path is a THIRD-PARTY-NOTICES.txt file.
    /// </summary>
    public static bool IsTpnPath(string path) => TpnFileName.IsMatch(path);
}
