// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Fabric;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using JetBrains.Annotations;
using Microsoft.DotNet.Internal.Health;
using Microsoft.DotNet.ServiceFabric.ServiceHost;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Win32;

namespace CoreHealthMonitor;

[UsedImplicitly(ImplicitUseKindFlags.InstantiatedNoFixedConstructorSignature)]
public sealed class CoreHealthMonitorService : IServiceImplementation
{
    private readonly IInstanceHealthReporter<CoreHealthMonitorService> _health;
    private readonly IOptions<DriveMonitorOptions> _driveOptions;
    private readonly IOptionsMonitor<MemoryDumpOptions> _memoryDumpOptions;
    private readonly ILogger<CoreHealthMonitorService> _logger;
    private readonly ServiceContext _context;
    private readonly ISystemClock _clock;
    private readonly Lazy<BlobContainerClient> _blobClient;

    public CoreHealthMonitorService(
        IInstanceHealthReporter<CoreHealthMonitorService> health,
        IOptions<DriveMonitorOptions> driveOptions,
        IOptionsMonitor<MemoryDumpOptions> memoryDumpOptions,
        ILogger<CoreHealthMonitorService> logger,
        ServiceContext context,
        ISystemClock clock)
    {
        _health = health;
        _driveOptions = driveOptions;
        _memoryDumpOptions = memoryDumpOptions;
        _logger = logger;
        _context = context;
        _clock = clock;
        _blobClient = new Lazy<BlobContainerClient>(CreateBlobClient);
    }

    private BlobContainerClient CreateBlobClient()
    {
        return new BlobContainerClient(new Uri(_memoryDumpOptions.CurrentValue.ContainerUri));
    }

    public async Task<TimeSpan> RunAsync(CancellationToken cancellationToken)
    {
        await ScanDriveFreeSpaceAsync().ConfigureAwait(false);
        await UploadMemoryDumpsAsync(cancellationToken).ConfigureAwait(false);

        return TimeSpan.FromMinutes(5);
    }

    private async Task ScanDriveFreeSpaceAsync()
    {
        foreach (DriveInfo drive in DriveInfo.GetDrives())
        {
            if (drive.DriveType != DriveType.Fixed)
            {
                _logger.LogInformation("Skipping drive {DriveName} of type {DriveType}", drive.Name, drive.DriveType);
                continue;
            }

            if (!drive.IsReady)
            {
                _logger.LogWarning("Drive {DriveName} reports !IsReady", drive.Name);
                continue;
            }

            long threshold;
            long freeSpace;
            try
            {
                threshold = _driveOptions.Value.MinimumFreeSpaceBytes;
                freeSpace = drive.AvailableFreeSpace;
            }
            catch (IOException e)
            {
                _logger.LogError(e, "Failed to get drive space information for drive {DriveName}", drive.Name);
                break;
            }

            if (freeSpace < threshold)
            {
                await _health.UpdateStatusAsync(
                        "DriveSpace:" + drive.Name,
                        HealthStatus.Error,
                        $"Available drive space on {drive.Name} is at {freeSpace:N} below threshold of {threshold:N} bytes"
                    )
                    .ConfigureAwait(false);
            }
            else
            {
                await _health.UpdateStatusAsync(
                        "DriveSpace:" + drive.Name,
                        HealthStatus.Healthy,
                        $"Available drive space on {drive.Name} is at {freeSpace:N} above threshold of {threshold:N} bytes"
                    )
                    .ConfigureAwait(false);
            }
        }
    }

    private async Task UploadMemoryDumpsAsync(CancellationToken cancellationToken)
    {
        string folder = Registry.GetValue(
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\Windows Error Reporting\LocalDumps",
            "DumpFolder",
            null
        ) as string;

        string containerUri = _memoryDumpOptions.CurrentValue.ContainerUri;
        if (string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(containerUri))
        {
            _logger.LogWarning("Memory dump monitoring settings not specified, skipping");
        }
        else if (!Directory.Exists(folder))
        {
            _logger.LogError("Memory dump directory '{folder}' does not exist", folder);
        }
        else
        {
            foreach (string file in Directory.GetFiles(folder))
            {
                string fileName = Path.GetFileName(file);
                if (IsIgnoredFile(fileName))
                {
                    continue;
                }

                DateTimeOffset now = _clock.UtcNow;
                string blobName =
                    $"{_context.NodeContext.NodeName}/{now:yyyy-MM}/{now:dd}/{now:yyyy-MM-ddTHH-mm-ss}-{fileName}";
                _logger.LogError(
                    "Found crash dump at '{crashDumpPath}', uploading to '{blobName}'",
                    file,
                    blobName
                );
                try
                {
                    await using (FileStream stream = File.OpenRead(file))
                    {
                        await _blobClient.Value.UploadBlobAsync(
                                blobName,
                                stream,
                                cancellationToken
                            )
                            .ConfigureAwait(false);
                    }
                    File.Delete(file);
                    _logger.LogInformation("Crash dump uploaded and deleted");
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Unable to upload and purge crash dump");
                }
            }
        }
    }

    private bool IsIgnoredFile(string fileName)
    {
        string[] patterns = _memoryDumpOptions.CurrentValue.IgnoreDumpPatterns;
        if (patterns == null)
        {
            return false;
        }

        return patterns.Any(pattern => Regex.IsMatch(fileName, pattern, RegexOptions.IgnoreCase));
    }
}
