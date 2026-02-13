// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;
using Microsoft.Extensions.Options;

namespace BuildInsights.AzureStorage.Cache;

public interface IBlobClientFactory
{
    BlobServiceClient CreateBlobServiceClient(string endpoint);
    BlobClient CreateBlobClient(string endpoint, string blobContainerName, string blobName);

    BlobLeaseClient CreateBlobLeaseClient(BlobClient blobClient, string leaseId = null);

    BlobContainerClient CreateBlobContainerClient(string endpoint, string blobContainerName);

}

public class BlobClientFactory : IBlobClientFactory
{
    private readonly ChainedTokenCredential _credential;

    public BlobClientFactory()
    {
        _credential = TokenCredentialHelper.GetChainedTokenCredential(string.Empty);
    }

    public BlobClientFactory(IOptions<ManagedIdentity> managedIdentity)
    {
        _credential = TokenCredentialHelper.GetChainedTokenCredential(managedIdentity.Value.ManagedIdentityId);
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

    public BlobLeaseClient CreateBlobLeaseClient(BlobClient blobClient, string leaseId = null)
    {
        return blobClient.GetBlobLeaseClient(leaseId);
    }
}
