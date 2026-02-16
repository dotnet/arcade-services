// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
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

internal class BlobContextualStorage : BaseContextualStorage, IDistributedLockService, IDisposable, IAsyncDisposable
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

    /// <summary>
    /// Returns a AzureBlobLease object after acquiring the lease.
    /// Will retry every 15 seconds until the lease is acquired OR the specified elapsed time is reached if the blob is already leased.
    /// </summary>
    /// <param name="lockName">Name of the blob to lease</param>
    /// <param name="maxLeaseWaitTime">Max time to spend retrying to acquire the lease</param>
    /// <param name="cancellationToken">CancellationToken</param>
    public async Task<IDistributedLock> AcquireAsync(string lockName, TimeSpan maxLeaseWaitTime, CancellationToken cancellationToken)
    {
        if (_distributedLockDictionary.TryGetValue(lockName, out IDistributedLock azureBlobLease))
        {
            // We can make the async local a dictionary if we really need to take multiple, different leases per thread
            throw new InvalidOperationException("A lease is already taken by this context.");
        }

        BlobClient blobClient = GetBlobClient(lockName);
        BlobLeaseClient blobLeaseClient = _blobClientFactory.CreateBlobLeaseClient(blobClient);
        BlobLease blobLease;

        var retryStopWatch = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                blobLease = await blobLeaseClient.AcquireAsync(TimeSpan.FromSeconds(60), null, cancellationToken);
                break;
            }
            catch (RequestFailedException r) when (r.Status == 404 && r.ErrorCode == "ContainerNotFound")
            {
                BlobContainerClient blobContainerClient = _blobClientFactory.CreateBlobContainerClient(_settings.CurrentValue.Endpoint, _settings.CurrentValue.ContainerName);
                await blobContainerClient.CreateAsync(cancellationToken: cancellationToken);
                continue;
            }
            catch (RequestFailedException r) when (r.Status == 404 && r.ErrorCode == "BlobNotFound")
            {
                // There was no blob to lease, lets create one
                try
                {
                    await blobClient.UploadAsync(new MemoryStream(), overwrite:false,  cancellationToken);
                }
                catch(RequestFailedException e)
                {
                    // There are lots of reasons we might fail to create the blob, it might exist,
                    // there might be a lease against it...
                    // But all we care about is that it exists
                    // And if we fail for a "real" reason, and it still doesn't exist
                    // we'll fail again when we try to get the lease anyway, so it's fine
                    _logger.LogInformation("Creating lease blob failed: {ExceptionMessage}", e.Message);
                }

                // Since the blob didn't exist, we are almost guaranteed to be the lock getter
                // so we don't want to waste time, so continue to avoid the delay
                continue;
            }
            catch (RequestFailedException r) when (r.Status == 409 && r.ErrorCode == "LeaseAlreadyPresent")
            {
                if (retryStopWatch.Elapsed > maxLeaseWaitTime)
                {
                    throw new TimeoutException();
                }
            }

            await Task.Delay(_settings.CurrentValue.LeaseAcquireRetryWaitTime, cancellationToken);
        }

        var renewalTokenSource = new CancellationTokenSource();
        var autoRenewalTask = Task.Run(() => RenewLeaseAsync(blobLeaseClient, blobLease.LeaseId, renewalTokenSource.Token));
        var newAzureBlobLease = new AzureBlobLease(this, blobClient, blobLeaseClient, blobLease.LeaseId, lockName, autoRenewalTask, renewalTokenSource);

        if(!_distributedLockDictionary.TryAdd(lockName, newAzureBlobLease))
        {
            _logger.LogError($"AzureBlobLease could not be added to dictionary for '{lockName}'");
        }

        return newAzureBlobLease;
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

    private async Task RenewLeaseAsync(BlobLeaseClient blobLeaseClient, string leaseId, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_settings.CurrentValue.LeaseRenewalTimespan, cancellationToken);
            }
            catch (OperationCanceledException e) when (e.CancellationToken == cancellationToken)
            {
                // Expected when every item completes, uninteresting, let it go
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to renew lease: '{leaseId}'", leaseId);
                throw;
            }

            if (cancellationToken.IsCancellationRequested)
                return;

            await blobLeaseClient.RenewAsync(new BlobRequestConditions
            {
                LeaseId = leaseId
            }, cancellationToken);
        }
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
            IDistributedLock distributedLock;
            if (_distributedLockDictionary.TryRemove(lockObj.Key, out distributedLock))
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
