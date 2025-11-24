// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Maestro.Common;
using Microsoft.DotNet.Darc.Options.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

internal class AddRepoOperation : Operation
{
    private readonly AddRepoCommandLineOptions _options;
    private readonly IVmrInitializer _vmrInitializer;
    private readonly IVmrInfo _vmrInfo;
    private readonly ILogger<AddRepoOperation> _logger;

    public AddRepoOperation(
        AddRepoCommandLineOptions options,
        IVmrInitializer vmrInitializer,
        IVmrInfo vmrInfo,
        ILogger<AddRepoOperation> logger)
    {
        _options = options;
        _vmrInitializer = vmrInitializer;
        _vmrInfo = vmrInfo;
        _logger = logger;
    }

    public override async Task<int> ExecuteAsync()
    {
        var repositories = _options.Repositories.ToList();

        if (repositories.Count == 0)
        {
            _logger.LogError("Please specify at least one repository to add");
            return Constants.ErrorCode;
        }

        // Repository names are in the form of URI:REVISION where URI is the git repository URL
        // and REVISION is a git ref (commit SHA, branch, or tag)
        foreach (var repository in repositories)
        {
            var parts = repository.Split(':', 2);
            if (parts.Length != 2)
            {
                _logger.LogError("Repository '{repository}' must be in the format URI:REVISION", repository);
                return Constants.ErrorCode;
            }

            string uri = parts[0];
            string revision = parts[1];

            // For URIs starting with https://, we need to reconstruct the full URI
            // since the split on ':' would have separated it
            if (uri == "https" || uri == "http")
            {
                // The original input had a URI with protocol, find the last : to split properly
                int lastColonIndex = repository.LastIndexOf(':');
                if (lastColonIndex <= uri.Length + 2) // +2 for "://"
                {
                    _logger.LogError("Repository '{repository}' must be in the format URI:REVISION", repository);
                    return Constants.ErrorCode;
                }
                
                uri = repository.Substring(0, lastColonIndex);
                revision = repository.Substring(lastColonIndex + 1);
            }

            try
            {
                // Extract repo name from URI
                var (repoName, _) = GitRepoUrlUtils.GetRepoNameAndOwner(uri);

                var sourceMappingsPath = _vmrInfo.VmrPath / VmrInfo.DefaultRelativeSourceMappingsPath;

                await _vmrInitializer.InitializeRepository(
                    repoName,
                    revision,
                    uri,
                    sourceMappingsPath,
                    new CodeFlowParameters(
                        Array.Empty<AdditionalRemote>(),
                        VmrInfo.ThirdPartyNoticesFileName,
                        GenerateCodeOwners: false,
                        GenerateCredScanSuppressions: true),
                    CancellationToken.None);

                _logger.LogInformation("Successfully added repository '{repoName}' from '{uri}' at revision '{revision}'", repoName, uri, revision);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to add repository from '{uri}'", uri);
                return Constants.ErrorCode;
            }
        }

        return Constants.SuccessCode;
    }
}
