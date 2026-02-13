// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable enable

using Azure.Storage.Blobs;
using Microsoft.Internal.Helix.Machines.MatrixOfTruthOutputDeserialization.V1.Models;
using Microsoft.Internal.Helix.Machines.MatrixOfTruthOutputDeserialization.V1;

namespace BuildInsights.QueueInsights;

public class MatrixOfTruthOutputV1Downloader
{
    private readonly BlobContainerClient _blobContainerClient;

    public MatrixOfTruthOutputV1Downloader(BlobContainerClient blobContainerClient)
    {
        _blobContainerClient = blobContainerClient;
    }

    public async Task<IList<PipelineOutputModel>?> DownloadPipelineOutput()
        => await DownloadAndDeserializeAsync("pipelineOutput.csv", MatrixOfTruthOutputV1Deserializer.DeserializePipelineOutput);

    public async Task<IList<EnvironmentDataOutputModel>?> DownloadEnvironmentDataOutput()
        => await DownloadAndDeserializeAsync("environmentDataOutput.csv", MatrixOfTruthOutputV1Deserializer.DeserializeEnvironmentDataOutput);

    public async Task<IList<EndOfLifeOutputModel>?> DownloadEndOfLifeOutput()
        => await DownloadAndDeserializeAsync("endOfLifeOutput.csv", MatrixOfTruthOutputV1Deserializer.DeserializeEndOfLifeOutput);

    public async Task<IList<SupportedDotNetPerOperatingSystemCsvOutputModel>?> DownloadSupportedDotNetPerOperatingSystemCsvOutputModel()
        => await DownloadAndDeserializeAsync("supportedDotNetVersionPerOperatingSystemOutput.csv", MatrixOfTruthOutputV1Deserializer.DeserializeSupportedDotNetPerOperatingSystemCsvOutput);

    public async Task<FullJsonOutput?> DownloadFullJsonOutput()
        => await DownloadAndDeserializeAsync("fullOutput.json", MatrixOfTruthOutputV1Deserializer.DeserializeFullOutput);

    public async Task<FullJsonOutput?> DownloadExecutionEnvironmentSupportOutput()
        => await DownloadAndDeserializeAsync("executionEnvironmentSupportOutput.json", MatrixOfTruthOutputV1Deserializer.DeserializeExecutionEnvironmentSupport);

    public async Task<SupportedDotNetPerOperatingSystemJsonOutput?> DownloadSupportedDotNetPerOperatingSystemJsonOutput()
        => await DownloadAndDeserializeAsync("supportedDotNetVersionPerOperatingSystemOutput.json", MatrixOfTruthOutputV1Deserializer.DeserializeSupportedDotNetPerOperatingSystemJsonOutput);

    public async Task<IList<MobileDeviceOutputModel>?> DownloadMobileDeviceOutputModel()
        => await DownloadAndDeserializeAsync("mobileDeviceOutput.csv", MatrixOfTruthOutputV1Deserializer.DeserializeMobileDeviceOutput);

    private async Task<T> DownloadAndDeserializeAsync<T>(string blobName, Func<StreamReader, T> deserialize)
    {
        using var streamReader = await DownloadAsync(blobName);
        return deserialize(streamReader);
    }

    private async Task<T> DownloadAndDeserializeAsync<T>(string blobName, Func<string, T> deserialize)
    {
        using var streamReader = await DownloadAsync(blobName);
        return deserialize(await streamReader.ReadToEndAsync());
    }

    private async Task<StreamReader> DownloadAsync(string blobName)
    {
        var blobClient = _blobContainerClient.GetBlobClient(blobName);
        var download = await blobClient.DownloadAsync();
        return new StreamReader(download.Value.Content);
    }
}
