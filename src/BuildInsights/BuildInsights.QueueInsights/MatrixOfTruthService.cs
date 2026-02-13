// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using BuildInsights.QueueInsights.Models;
using Microsoft.Extensions.Options;
using Microsoft.Internal.Helix.Machines.MatrixOfTruthOutputDeserialization.V1.Models;

namespace BuildInsights.QueueInsights;

public interface IMatrixOfTruthService
{
    public Task<IList<PipelineOutputModel>> GetPipelineOutputsAsync();
}

public class MatrixOfTruthService : IMatrixOfTruthService
{
    private readonly IOptionsMonitor<MatrixOfTruthSettings> _settings;
    private readonly IBlobClientFactory _blobClientFactory;

    public MatrixOfTruthService(IOptionsMonitor<MatrixOfTruthSettings> settings, IBlobClientFactory blobClientFactory)
    {
        _settings = settings;
        _blobClientFactory = blobClientFactory;
    }

    public async Task<IList<PipelineOutputModel>> GetPipelineOutputsAsync()
    {
        var blobContainerClient = _blobClientFactory.CreateBlobContainerClient(_settings.CurrentValue.Endpoint, _settings.CurrentValue.ContainerName);
        var client = new MatrixOfTruthOutputV1Downloader(blobContainerClient);
        return await client.DownloadPipelineOutput();
    }
}
