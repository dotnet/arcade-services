// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BuildInsights.AzureStorage.Cache;

internal class BlobContextualStorage : BaseContextualStorage, IDisposable, IAsyncDisposable
{
    private readonly IOptionsMonitor<BlobStorageSettings> _settings;
    private readonly IBlobClientFactory _blobClientFactory;
    private readonly ConcurrentDictionary<string, IDistributedLock> _distributedLockDictionary = [];
    private readonly ILogger<BlobContextualStorage> _logger;

    public BlobContextualStorage(
        IOptionsMonitor<BlobStorageSettings> settings,
        IBlobClientFactory blobClientFactory,
        ILogger<BlobContextualStorage> logger)
    {
        _settings = settings;
        _blobClientFactory = blobClientFactory;
        _logger = logger;
    }

    private async Task ReleaseAsync(AzureBlobLease azureBlobLease)
    {
        azureBlobLease.RenewalTokenSource.Cancel();

        try
        {
            await azureBlobLease.AutoRenewalTask;
        }
        catch (OperationCanceledException e) when (e.CancellationToken == azureBlobLease.RenewalTokenSource.Token)
        {
            // Expected when every item completes, uninteresting, let it go
            throw;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error occured with the AutoRenewalTask");
            throw;
        }

        await azureBlobLease.BlobLeaseClient.ReleaseAsync(new BlobRequestConditions
        {
            LeaseId = azureBlobLease.LeaseId
        });

        _distributedLockDictionary.TryRemove(azureBlobLease.LockName, out _);
    }

    protected override async Task PutAsync(string root, string name, Stream data, CancellationToken cancellationToken)
    {
        BlobContainerClient blobContainerClient = _blobClientFactory.CreateBlobContainerClient(_settings.CurrentValue.Endpoint, _settings.CurrentValue.ContainerName);
        await blobContainerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

        BlobClient blobClient = GetBlobClient(root, name);

        _logger.LogInformation("Saving blob to {blobUrl}", blobClient.Uri);
        await blobClient.UploadAsync(data, true, cancellationToken);
    }

    protected override async Task<Stream> TryGetAsync(string root, string name, CancellationToken cancellationToken)
    {
        BlobClient blobClient = GetBlobClient(root, name);
        try
        {
            BlobDownloadInfo blob = await blobClient.DownloadAsync(cancellationToken);
            return blob.Content;
        }
        catch (RequestFailedException e) when (e.Status == 404)
        {
            return null;
        }
    }

    private BlobClient GetBlobClient(string root, string name)
    {
        return GetBlobClient($"{root}/{name}");
    }

    private BlobClient GetBlobClient(string lockName)
    {
        return _blobClientFactory.CreateBlobClient(_settings.CurrentValue.Endpoint, _settings.CurrentValue.ContainerName, lockName);
    }

    public void Dispose()
    {
        DisposeAsync().GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var lockObj in _distributedLockDictionary)
        {
            if (_distributedLockDictionary.TryRemove(lockObj.Key, out IDistributedLock distributedLock))
            {
                _logger.LogError("'{lockName}' was not properly disposed.", lockObj.Key);
                await distributedLock.DisposeAsync();
            }
        }
    }

    private class AzureBlobLease : IDistributedLock
    {
        public string LeaseId { get; }

        public string LockName { get; }

        public BlobClient BlobClient { get; }

        public BlobLeaseClient BlobLeaseClient { get; }

        public Task AutoRenewalTask { get; }

        public CancellationTokenSource RenewalTokenSource { get; }

        private BlobContextualStorage _parent;

        public AzureBlobLease(BlobContextualStorage parent,
            BlobClient blobClient,
            BlobLeaseClient blobleaseClient,
            string leaseId,
            string lockName,
            Task autoRenewalTask,
            CancellationTokenSource renewalTokenSource)
        {
            _parent = parent;
            BlobClient = blobClient;
            BlobLeaseClient = blobleaseClient;
            LeaseId = leaseId;
            LockName = lockName;
            AutoRenewalTask = autoRenewalTask;
            RenewalTokenSource = renewalTokenSource;
        }

        public void Dispose()
        {
            DisposeAsync().GetAwaiter().GetResult();
        }

        public async ValueTask DisposeAsync()
        {
            var parent = Interlocked.Exchange(ref _parent, null);
            if (parent != null)
                await parent.ReleaseAsync(this);
        }
    }
}
