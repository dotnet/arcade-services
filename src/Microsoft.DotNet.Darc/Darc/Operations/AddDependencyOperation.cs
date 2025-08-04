// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.Extensions.Logging;

namespace Microsoft.DotNet.Darc.Operations;

internal class AddDependencyOperation : Operation
{
    private readonly AddDependencyCommandLineOptions _options;
    private readonly ILogger<AddDependencyOperation> _logger;

    public AddDependencyOperation(
        AddDependencyCommandLineOptions options,
        ILogger<AddDependencyOperation> logger)
    {
        _options = options;
        _logger = logger;
    }

    public override async Task<int> ExecuteAsync()
    {
        DependencyType type = _options.Type.ToLower() == "toolset" ? DependencyType.Toolset : DependencyType.Product;

        var local = new Local(_options.GetRemoteTokenProvider(), _logger);

        var dependency = new DependencyDetail
        {
            Name = _options.Name,
            Version = _options.Version ?? string.Empty,
            RepoUri = _options.RepoUri ?? string.Empty,
            Commit = _options.Commit ?? string.Empty,
            CoherentParentDependencyName = _options.CoherentParentDependencyName ?? string.Empty,
            Pinned = _options.Pinned,
            SkipProperty = _options.SkipProperty,
            Type = type,
        };

        try
        {
            await local.AddDependencyAsync(dependency);
            return Constants.SuccessCode;
        }
        catch (FileNotFoundException exc)
        {
            _logger.LogError(exc, $"One of the version files is missing. Please make sure to add all files " +
                                 "included in https://github.com/dotnet/arcade/blob/main/Documentation/DependencyDescriptionFormat.md#dependency-description-details");
            return Constants.ErrorCode;
        }
        catch (Exception exc)
        {
            _logger.LogError(exc, $"Failed to add dependency '{dependency.Name}' to repository.");
            return Constants.ErrorCode;
        }
    }
}
