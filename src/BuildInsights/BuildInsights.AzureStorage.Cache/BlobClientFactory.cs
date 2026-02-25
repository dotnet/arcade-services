// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Azure.Core;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;
using Maestro.Common.AppCredentials;
using Microsoft.Extensions.Options;

namespace BuildInsights.AzureStorage.Cache;

public interface IBlobClientFactory
{
    BlobServiceClient CreateBlobServiceClient(string endpoint);

    BlobClient CreateBlobClient(string endpoint, string blobContainerName, string blobName);

    BlobLeaseClient CreateBlobLeaseClient(BlobClient blobClient, string? leaseId = null);

    BlobContainerClient CreateBlobContainerClient(string endpoint, string blobContainerName);

}

public class BlobClientFactory : IBlobClientFactory
{
    private readonly TokenCredential _credential;

    public BlobClientFactory(IOptions<BlobStorageSettings> credentialOptions)
    {
        _credential = CredentialResolver.CreateCredential(credentialOptions.Value);
    }

    public BlobServiceClient CreateBlobServiceClient(string endpoint)
    {
        return new BlobServiceClient(new Uri(endpoint), _credential);
    }

    public BlobClient CreateBlobClient(string endpoint, string blobContainerName, string blobName)
    {
        return new BlobClient(new Uri($"{endpoint}/{blobContainerName}/{blobName}"), _credential);
    }

    public BlobContainerClient CreateBlobContainerClient(string endpoint, string blobContainerName)
    {
        return new BlobContainerClient(new Uri($"{endpoint}/{blobContainerName}"), _credential);
    }

    public BlobLeaseClient CreateBlobLeaseClient(BlobClient blobClient, string? leaseId = null)
    {
        return blobClient.GetBlobLeaseClient(leaseId);
    }
}
