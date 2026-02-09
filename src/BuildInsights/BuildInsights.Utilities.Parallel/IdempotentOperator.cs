// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;

namespace BuildInsights.Utilities.Parallel;

public class IdempotentOperator<TKey, TContext, TInput, TResult> where TKey : IEquatable<TKey>
{
    private class Context
    {
        public SemaphoreSlim ExecutingSemaphore { get; private set; } = new SemaphoreSlim(0, 1);
        public TResult Result { get; set; }
        public bool Completed { get; set; }

        public void SetComplete(TResult result)
        {
            Result = result;
            Completed = true;
            // We can't dispose it here, because other people might be waiting on us
            // But we can let the GC have it
            ExecutingSemaphore = null;
        }
    }

    private readonly ExpiringDictionary<TKey, Context> _execution;
    private readonly Func<TContext, TInput, IdempotentOperationContext, Task<TResult>> _callback;

    // If we have a Telemetry Client, log info about key used and whether this instance actually performed the action
    // See https://github.com/dotnet/core-eng/issues/12339 for context; if clients end up talking to > 1 endpoint on
    // a retried session we need to write down idempotency information somewhere shared across all HelixAPI instances.
    public TelemetryClient AppInsightsTelemetryClient { get; set; }

    public IdempotentOperator(Func<TContext, TInput, IdempotentOperationContext, Task<TResult>> callback, TimeSpan minimumAgeToKeep)
    {
        _callback = callback;
        _execution = new ExpiringDictionary<TKey, Context>(minimumAgeToKeep);
    }

    public Task<TResult> ExecuteUnconditionallyAsync(TContext context, TInput input) => _callback(context, input, new IdempotentOperationContext());

    public async Task<TResult> ExecuteAsync(TKey key, TContext context, TInput input)
    {
        async Task<TResult> RunCallback(Context ec)
        {
            var ctx = new IdempotentOperationContext();
            var result = await _callback(context, input, ctx);
            if (ctx.CacheResult)
                ec.SetComplete(result);
            return result;
        }

        if (_execution.TryAdd(key, () => new Context(), out Context executionContext))
        {
            // If we added it, we are the executors, (also, the semaphore is pre-waited, so we don't need to wait there
            var semaphore = executionContext.ExecutingSemaphore;
            try
            {
                LogIdempotencyStatistics(true, key);
                return await RunCallback(executionContext);
            }
            finally
            {
                // Whether we succeeded or not, it's someone elses turn
                semaphore.Release();
            }
        }

        if (executionContext.Completed)
        {
            // Oh, someone else already did it, sweet, return it
            LogIdempotencyStatistics(false, key);
            return executionContext.Result;
        }

        // We need to wait for our turn to try (in case two calls are in here together)
        using (await SemaphoreLock.LockAsync(executionContext.ExecutingSemaphore))
        {
            if (executionContext.Completed)
            {
                // Someone completed it while we were waiting, nice, return their answer
                // (got here the same time as someone else, they finished it for me)
                LogIdempotencyStatistics(false, key);
                return executionContext.Result;
            }

            // Whatever that other guy did, he didn't finish, so it's our turn
            LogIdempotencyStatistics(true, key);
            return await RunCallback(executionContext);
        }
    }

    private void LogIdempotencyStatistics(bool executed, TKey key)
    {
        if (AppInsightsTelemetryClient != null)
        {
            // IdempotentOperation {key:key, type:"TInput->TResult", executed:true/false}
            var idempotencyCheckInfo = new EventTelemetry("IdempotentOperation");
            idempotencyCheckInfo.Properties.Add("key", $"{key}");
            idempotencyCheckInfo.Properties.Add("type", $"{typeof(TInput)}->{typeof(TResult)}");
            idempotencyCheckInfo.Properties.Add("executed", $"{executed}");
            AppInsightsTelemetryClient.TrackEvent(idempotencyCheckInfo);
        }
    }
}
