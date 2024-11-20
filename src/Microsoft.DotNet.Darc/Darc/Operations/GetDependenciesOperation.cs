// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Helpers;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.Extensions.Logging;

namespace Microsoft.DotNet.Darc.Operations;

internal class GetDependenciesOperation : Operation
{
    private readonly GetDependenciesCommandLineOptions _options;
    private readonly ILogger<GetDependenciesOperation> _logger;

    public GetDependenciesOperation(
        GetDependenciesCommandLineOptions options,
        ILogger<GetDependenciesOperation> logger)
    {
        _options = options;
        _logger = logger;
    }

    public override async Task<int> ExecuteAsync()
    {
        var local = new Local(_options.GetRemoteTokenProvider(), _logger);

        try
        {
            IEnumerable<DependencyDetail> dependencies = await local.GetDependenciesAsync(_options.Name);

            if (!string.IsNullOrEmpty(_options.Name))
            {
                DependencyDetail dependency = dependencies
                    .Where(d => d.Name.Equals(_options.Name, StringComparison.InvariantCultureIgnoreCase))
                    .FirstOrDefault();

                if (dependency == null)
                {
                    throw new Exception($"A dependency with name '{_options.Name}' was not found...");
                }

                LogDependency(dependency);
            }

            foreach (DependencyDetail dependency in dependencies)
            {
                LogDependency(dependency);

                Console.WriteLine();
            }

            return Constants.SuccessCode;
        }
        catch (Exception exc)
        {
            if (!string.IsNullOrEmpty(_options.Name))
            {
                _logger.LogError(exc, $"Something failed while querying for local dependency '{_options.Name}'.");
            }
            else
            {
                _logger.LogError(exc, "Something failed while querying for local dependencies.");
            }
                
            return Constants.ErrorCode;
        }
    }

    private static void LogDependency(DependencyDetail dependency)
    {
        Console.Write(UxHelpers.DependencyToString(dependency));
    }
}
