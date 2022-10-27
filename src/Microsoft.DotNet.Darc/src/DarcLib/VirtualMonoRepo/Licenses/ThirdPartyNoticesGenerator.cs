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
    private static readonly char[] NewlineChars = { '\n', '\r' };
    private static readonly Regex TpnFileName = new(@"third-?party-?notices(.txt)?$", RegexOptions.IgnoreCase);

    private readonly IVmrInfo _vmrInfo;
    private readonly IVmrDependencyTracker _dependencyTracker;
    private readonly ILocalGitRepo _localGitClient;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<ThirdPartyNoticesGenerator> _logger;

    public ThirdPartyNoticesGenerator(
        IVmrInfo vmrInfo,
        IVmrDependencyTracker dependencyTracker,
        ILocalGitRepo localGitClient,
        IFileSystem fileSystem,
        ILogger<ThirdPartyNoticesGenerator> logger)
    {
        _vmrInfo = vmrInfo;
        _dependencyTracker = dependencyTracker;
        _localGitClient = localGitClient;
        _fileSystem = fileSystem;
        _logger = logger;
    }

    public async Task UpdateThirtPartyNotices()
    {
        if (!NeedsUpdate())
        {
            return;
        }
        
        _logger.LogInformation("Updating {tpnName} because there are new updates...", VmrInfo.ThirdPartyNoticesFileName);

        var vmrTpnPath = _fileSystem.PathCombine(_vmrInfo.VmrPath, VmrInfo.ThirdPartyNoticesFileName);
        var vmrTpn = _fileSystem.FileExists(vmrTpnPath)
            ? TpnDocument.Parse((await _fileSystem.ReadAllTextAsync(vmrTpnPath)).Split(NewlineChars))
            : new TpnDocument(string.Empty, Array.Empty<TpnSection>());

        // TODO: Remove?
        foreach (var s in vmrTpn.Sections.OrderBy(s => s.Header.SingleLineName))
        {
            _logger.LogDebug($"{s.Header.StartLine + 1}:{s.Header.StartLine + s.Header.LineLength} {s.Header.Format} '{s.Header.SingleLineName}'");
        }

        var tpns = new List<TpnDocument>();

        foreach (var notice in GetAllNotices())
        {
            _logger.LogDebug("Processing {name}...", notice);

            var content = await _fileSystem.ReadAllTextAsync(notice);
            tpns.Add(TpnDocument.Parse(content.Split(NewlineChars)));
        }

        TpnSection[] newSections = tpns
            .SelectMany(o => o.Sections)
            .Except(vmrTpn.Sections, new TpnSection.ByHeaderNameComparer())
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

        var newTpn = new TpnDocument(vmrTpn.Preamble, vmrTpn.Sections.Concat(newSections));
        _fileSystem.WriteToFile(vmrTpnPath, newTpn.ToString());

        _logger.LogInformation("{tpnName} updated", VmrInfo.ThirdPartyNoticesFileName);
        _localGitClient.Stage(_vmrInfo.VmrPath, VmrInfo.ThirdPartyNoticesFileName);
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

            foreach (var file in _fileSystem.GetFiles(possiblePath))
            {
                if (TpnFileName.IsMatch(file))
                {
                    yield return file;
                }
            }
        }
    }

    /// <summary>
    /// Checkes whether at least one third party notice has been changed in the current change.
    /// </summary>
    private bool NeedsUpdate() =>
        _localGitClient
            .GetStagedFiles(_vmrInfo.VmrPath)
            .Where(path => TpnFileName.IsMatch(path))
            .Any();
}
