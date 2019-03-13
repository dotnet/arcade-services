// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Microsoft.DotNet.Darc.Operations
{
    internal class AddDependencyOperation : Operation
    {
        AddDependencyCommandLineOptions _options;
        public AddDependencyOperation(AddDependencyCommandLineOptions options)
            : base(options)
        {
            _options = options;
        }

        public override async Task<int> ExecuteAsync()
        {
            DependencyType type = _options.Type.ToLower() == "toolset" ? DependencyType.Toolset : DependencyType.Product;

            Local local = new Local(Logger);

            DependencyDetail dependency = new DependencyDetail
            {
                Name = _options.Name,
                Version = _options.Version ?? string.Empty,
                RepoUri = _options.RepoUri ?? string.Empty,
                Commit = _options.Commit ?? string.Empty,
                CoherentParentDependencyName = _options.CoherentParentDependencyName ?? string.Empty,
                Pinned = _options.Pinned,
                Type = type,
            };

            try
            {
                await local.AddDependencyAsync(dependency);
                return Constants.SuccessCode;
            }
            catch (FileNotFoundException exc)
            {
                Logger.LogError(exc, $"One of the version files is missing. Please make sure to add all files " +
                    "included in https://github.com/dotnet/arcade/blob/master/Documentation/DependencyDescriptionFormat.md#dependency-description-details");
                return Constants.ErrorCode;
            }
            catch (Exception exc)
            {
                Logger.LogError(exc, $"Failed to add dependency '{dependency.Name}' to repository.");
                return Constants.ErrorCode;
            }
        }
    }
}
