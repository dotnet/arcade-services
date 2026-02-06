// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Internal.Helix.BuildFailureAnalysis.Models;
using Microsoft.Internal.Helix.BuildFailureAnalysis.Services;
using Microsoft.Internal.Helix.Utility;
using Microsoft.Internal.Helix.Utility.Azure;

namespace Microsoft.Internal.Helix.BuildFailureAnalysis.Providers
{
    [DependencyInjected]
    public class BuildCacheProvider : IBuildCacheService
    {
        private readonly JsonSerializerOptions _options = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private readonly IContextualStorage _storage;

        public BuildCacheProvider(IContextualStorage storage)
        {
            _storage = storage;
        }

        public async Task<BuildResultAnalysis> TryGetBuildAsync(BuildReferenceIdentifier build, CancellationToken cancellationToken)
        {
            await using Stream stream = await _storage.TryGetAsync(build.BuildId.ToString(), cancellationToken);
            if (stream == null)
                return null;
            return await JsonSerializer.DeserializeAsync<BuildResultAnalysis>(stream, _options, cancellationToken);
        }

        public async Task PutBuildAsync(BuildReferenceIdentifier build, BuildResultAnalysis analysis, CancellationToken cancellationToken)
        {
            await Helpers.StreamDataAsync(
                s => JsonSerializer.SerializeAsync(s, analysis, _options, cancellationToken),
                s => _storage.PutAsync(build.BuildId.ToString(), s, cancellationToken)
            );
        }
    }
}
