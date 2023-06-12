// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.Darc;

/// <summary>
/// Listens for user's key presses and triggers a cancellation when ESC / Space is pressed.
/// This is used for graceful cancellation to not leave things behind in some inconsistent state.
/// </summary>
internal class CancellationKeyListener : IDisposable
{
    private readonly CancellationTokenSource _cancellationSource;

    public CancellationToken Token => _cancellationSource.Token;

    public bool CancelledByKeyPress { get; private set; }

    private CancellationKeyListener(CancellationTokenSource cancellationSource)
    {
        _cancellationSource = cancellationSource;
    }

    public static CancellationKeyListener ListenForCancellation(ILogger logger)
    {
        var cancellationSource = new CancellationTokenSource();
        var listener = new CancellationKeyListener(cancellationSource);

        // Key read might not be available in all scenarios
        if (Console.IsInputRedirected)
        {
            return listener;
        }

        void CancelRun()
        {
            logger.LogWarning("Run interrupted by user, stopping synchronization...");
            cancellationSource.Cancel();
        }

        Console.CancelKeyPress += new ConsoleCancelEventHandler((object? sender, ConsoleCancelEventArgs args) =>
        {
            args.Cancel = true;
            listener.CancelledByKeyPress = true;
            CancelRun();
        });

        new Thread(() =>
        {
            ConsoleKeyInfo keyInfo;

            do
            {
                while (!Console.KeyAvailable)
                {
                    // We were cancelled from the outside (Dispose), we need to kill the thread
                    if (cancellationSource.IsCancellationRequested)
                    {
                        return;
                    }

                    Thread.Sleep(250);
                }

                keyInfo = Console.ReadKey(true);
            } while (keyInfo.Key != ConsoleKey.Escape && keyInfo.Key != ConsoleKey.Spacebar);

            CancelRun();
        })
        {
            IsBackground = true
        }.Start();

        return listener;
    }

    public void Dispose()
    {
        // Cancel the listening to not leave a running thread behind
        if (!_cancellationSource.IsCancellationRequested)
        {
            _cancellationSource.Cancel();
        }

        _cancellationSource.Dispose();
    }
}
