// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.Logging;

namespace Microsoft.DotNet.Build.Tasks
{
    public class RegenerateThirdPartyNotices
    {
        private static readonly char[] NewlineChars = { '\n', '\r' };
        private static readonly Regex TpnFileName = new(@"third-?party-?notices(.txt)?$", RegexOptions.IgnoreCase);

        private readonly IVmrInfo _vmrInfo;
        private readonly IVmrDependencyTracker _dependencyTracker;
        private readonly ILocalGitRepo _localGitClient;
        private readonly IFileSystem _fileSystem;
        private readonly ILogger<RegenerateThirdPartyNotices> _logger;

        public RegenerateThirdPartyNotices(
            IVmrInfo vmrInfo,
            IVmrDependencyTracker dependencyTracker,
            ILocalGitRepo localGitClient,
            IFileSystem fileSystem,
            ILogger<RegenerateThirdPartyNotices> logger)
        {
            _vmrInfo = vmrInfo;
            _dependencyTracker = dependencyTracker;
            _localGitClient = localGitClient;
            _fileSystem = fileSystem;
            _logger = logger;
        }

        public void UpdateThirtPartyNotices()
        {
            var modifiedNotices = _localGitClient
                .GetStagedFiles(_vmrInfo.VmrPath)
                .Where(path => TpnFileName.IsMatch(path))
                .Where(_fileSystem.FileExists);

            if (!modifiedNotices.Any())
            {
                return;
            }

            _logger.LogInformation("Updating {tpnName}", VmrInfo.ThirdPartyNoticesFileName);

            foreach (var notice in modifiedNotices)
            {
                _logger.LogDebug("Processing {name}...", notice);

                // var content = await _fileSystem.ReadAllTextAsync(notice);
                // var tpn = TpnDocument.Parse(content.Split(NewlineChars));

                
            }

            _logger.LogInformation("{tpnName} updated", VmrInfo.ThirdPartyNoticesFileName);
            // _localGitClient.Stage(_vmrInfo.VmrPath, VmrInfo.ThirdPartyNoticesFileName);
        }

        /*public async Task ExecuteAsync(HttpClient client)
        {
            // Ensure we found one (and only one) TPN file for each repo.
            foreach (var miscount in results
                .GroupBy(r => r.Repo)
                .Where(g => g.Count(r => r.Content != null) != 1))
            {
                Log.LogError($"Unable to find exactly one TPN for {miscount.Key}");
            }

            if (Log.HasLoggedErrors)
            {
                return;
            }

            TpnDocument existingTpn = TpnDocument.Parse(File.ReadAllLines(TpnFile));

            Log.LogMessage(
                MessageImportance.High,
                $"Existing TPN file preamble: {existingTpn.Preamble.Substring(0, 10)}...");

            foreach (var s in existingTpn.Sections.OrderBy(s => s.Header.SingleLineName))
            {
                Log.LogMessage(
                    MessageImportance.High,
                    $"{s.Header.StartLine + 1}:{s.Header.StartLine + s.Header.LineLength} {s.Header.Format} '{s.Header.SingleLineName}'");
            }

            TpnDocument[] otherTpns = results
                .Select(r => r.Content)
                .Where(r => r != null)
                .ToArray();

            TpnSection[] newSections = otherTpns
                .SelectMany(o => o.Sections)
                .Except(existingTpn.Sections, new TpnSection.ByHeaderNameComparer())
                .OrderBy(s => s.Header.Name)
                .ToArray();

            foreach (TpnSection existing in results
                .SelectMany(r => (r.Content?.Sections.Except(newSections)).NullAsEmpty())
                .Where(s => !newSections.Contains(s))
                .OrderBy(s => s.Header.Name))
            {
                Log.LogMessage(
                    MessageImportance.High,
                    $"Found already-imported section: '{existing.Header.SingleLineName}'");
            }

            foreach (var s in newSections)
            {
                Log.LogMessage(
                    MessageImportance.High,
                    $"New section to import: '{s.Header.SingleLineName}' of " +
                    string.Join(
                        ", ",
                        results
                            .Where(r => r.Content?.Sections.Contains(s) == true)
                            .Select(r => r.Url)) +
                    $" line {s.Header.StartLine}");
            }

            Log.LogMessage(MessageImportance.High, $"Importing {newSections.Length} sections...");

            var newTpn = new TpnDocument
            {
                Preamble = existingTpn.Preamble,
                Sections = existingTpn.Sections.Concat(newSections)
            };

            File.WriteAllText(TpnFile, newTpn.ToString());

            Log.LogMessage(MessageImportance.High, $"Wrote new TPN contents to {TpnFile}.");
        }*/
    }
}
