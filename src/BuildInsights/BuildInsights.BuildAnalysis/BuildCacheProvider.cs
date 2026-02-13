// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;
using System.Text.Json.Serialization;
using BuildInsights.AzureStorage.Cache;
using BuildInsights.BuildAnalysis.Models;

namespace BuildInsights.BuildAnalysis;

public interface IBuildCacheService
{
    Task<BuildResultAnalysis> TryGetBuildAsync(BuildReferenceIdentifier build, CancellationToken cancellationToken);
    Task PutBuildAsync(BuildReferenceIdentifier build, BuildResultAnalysis analysis, CancellationToken cancellationToken);
}

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
