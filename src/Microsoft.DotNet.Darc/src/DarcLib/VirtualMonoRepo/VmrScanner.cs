using Microsoft.DotNet.Darc.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo
{
    public class VmrScanner : IVmrScanner
    {
        private const string _zeroCommitTag = "zeroCommit";

        private readonly IReadOnlyCollection<SourceMapping> _sourceMappings;
        private readonly IProcessManager _processManager;
        private readonly IVmrInfo _vmrInfo;
        private readonly ILogger<VmrScanner> _logger;

        public VmrScanner(
            IReadOnlyCollection<SourceMapping> sourceMappings,
            IProcessManager processManager,
            IVmrInfo vmrInfo,
            ILogger<VmrScanner> logger)
        {
            _sourceMappings = sourceMappings;
            _processManager = processManager;
            _vmrInfo = vmrInfo;
            _logger = logger;
        }

        public async Task ScanVmr()
        {
            await CreateZeroCommitTag();

            foreach (var sourceMapping in _sourceMappings)
            {
                await ScanSubRepository(sourceMapping);
            }
        }

        private async Task ScanSubRepository(SourceMapping sourceMapping)
        {
            _logger.LogInformation("Scanning {repository} repository", sourceMapping.Name);
            var args = new List<string>
            {
                "diff",
                "--name-only",
                _zeroCommitTag
            };

            var baseExcludePath = _vmrInfo.GetRepoSourcesPath(sourceMapping);
            var preservedFiles = GetVmrPreservedFiles(sourceMapping);

            foreach (var exclude in sourceMapping.Exclude)
            {
                args.Add(baseExcludePath / exclude);
            }

            var ret = await _processManager.ExecuteGit(_vmrInfo.VmrPath, args.ToArray());

            ret.ThrowIfFailed($"Failed to scan the {sourceMapping.Name} repository");
            var files = ret.StandardOutput
                .Split("\r\n")
                .Where(file => file != string.Empty)
                .Select(file => _vmrInfo.VmrPath / file);
                
            foreach (var file in files)
            {
                if (preservedFiles.Contains(file))
                {
                    continue;
                }
                _logger.LogWarning("Found file {file} that should be exluded from the {repository} repository", file.ToString(), sourceMapping.Name);
            }

            await DeleteZeroCommitTag();
        }

        private async Task CreateZeroCommitTag()
        {
            await DeleteZeroCommitTag();

            var args = new[]
            {
                "hash-object",
                "-t",
                "tree",
                "/dev/null",
            };
            var zeroCommitResult = await _processManager.ExecuteGit(_vmrInfo.VmrPath, args);
            var zeroCommitSha = zeroCommitResult.StandardOutput.TrimEnd('\n', '\r');

            args = new[]
            {
                "tag",
                _zeroCommitTag,
                zeroCommitSha
            };
            var res = await _processManager.ExecuteGit(_vmrInfo.VmrPath, args);

            res.ThrowIfFailed($"Error creating {_zeroCommitTag} git tag: {res.StandardError}");
        }

        private async Task DeleteZeroCommitTag()
        {
            var args = new[]
            {
                "tag",
                "-d",
                _zeroCommitTag
            };

            await _processManager.ExecuteGit(_vmrInfo.VmrPath, args);
        }

        private HashSet<NativePath> GetVmrPreservedFiles(SourceMapping sourceMapping)
        {
            var files = Directory.GetFiles(_vmrInfo.GetRepoSourcesPath(sourceMapping), ".gitattributes", SearchOption.AllDirectories);

            return files.Select(file =>
                (fileName: file, Attributes: File.ReadAllLines(file)
                            .Where(line => line.Contains("vmr-preserve"))
                            .Select(line => line.Split(" ").First())
                            .ToList()))
                    .Where(entry => entry.Attributes.Count > 0)
                    .SelectMany(entry => entry.Attributes
                        .Select(attribute => new NativePath(Path.Join(Path.GetDirectoryName(entry.fileName), attribute))))
                    .ToHashSet();
        }
    }
}
