// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Concurrent;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;

namespace QueueInsights;

public interface IBlobClientFactory
{
    BlobClient CreateBlobClient(string endpoint, string blobContainerName, string blobName);
    BlobContainerClient CreateBlobContainerClient(string endpoint, string blobContainerName);
    BlobLeaseClient CreateBlobLeaseClient(BlobClient blobClient, string leaseId = null);
    BlobServiceClient CreateBlobServiceClient(string endpoint);
}

// TODO: Do we even need this? Should this be in some common project?
public class BlobClientFactory : IBlobClientFactory
{
    private readonly ChainedTokenCredential _credential;

    public BlobClientFactory()
    {
        _credential = TokenCredentialHelper.GetChainedTokenCredential(string.Empty);
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


// TODO: Do we even need this? Should this be in some common project?
public static class TokenCredentialHelper
{
    private static readonly ChainedTokenCredential _defaultCredential = new(
        new DefaultAzureCredential(
            new DefaultAzureCredentialOptions
            {
                ExcludeEnvironmentCredential = true
            }
        )
    );

    private static ConcurrentDictionary<string, ChainedTokenCredential> CredentialCache { get; } = new ConcurrentDictionary<string, ChainedTokenCredential>();

    public static ChainedTokenCredential GetChainedTokenCredential(string managedIdentityId)
    {
        if (managedIdentityId == null)
        {
            return _defaultCredential;
        }

        if (CredentialCache.TryGetValue(managedIdentityId, out var chainedTokenCredential))
        {
            return chainedTokenCredential;
        }

        var credential = new ChainedTokenCredential(new ManagedIdentityCredential(managedIdentityId), new AzureCliCredential(), _defaultCredential);

        CredentialCache.TryAdd(managedIdentityId, credential);

        return credential;
    }
}
