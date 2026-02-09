// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace BuildInsights.Utilities.Parallel;

public static class LimitedParallel
{
    public static Task<TResult[]> WhenAll<TSource, TResult>(
        IList<TSource> source,
        Func<TSource, Task<TResult>> executor,
        int maxParallelism)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(executor);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxParallelism);

        if (source.Count == 0)
            return Task.FromResult(Array.Empty<TResult>());

        var concurrencyLimit = new SemaphoreSlim(maxParallelism);

        async Task<TResult> ExecuteBlocked(TSource item)
        {
            await concurrencyLimit.WaitAsync();
            try
            {
                return await executor(item);
            }
            finally
            {
                concurrencyLimit.Release();
            }
        }

        return Task.WhenAll(source.Select(ExecuteBlocked));
    }

    public static IAsyncEnumerable<TResult> WhenAllAsync<TSource, TResult>(IEnumerable<TSource> enumerable, Func<TSource, Task<TResult>> translate, int parallelism)
        => WhenAllAsync(enumerable.ToAsyncEnumerable(), translate, parallelism);

    public static async IAsyncEnumerable<TResult> WhenAllAsync<TSource, TResult>(
        IAsyncEnumerable<TSource> enumerable,
        Func<TSource, Task<TResult>> translate, int parallelism)
    {
        ArgumentNullException.ThrowIfNull(enumerable);

        ArgumentNullException.ThrowIfNull(translate);

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(parallelism);

        var buffer = new List<Task<TResult>>(parallelism);
        await foreach (var item in enumerable)
        {
            if (buffer.Count == buffer.Capacity)
            {
                Task<TResult> completed = await Task.WhenAny(buffer);
                int i = buffer.IndexOf(completed);
                yield return await completed;
                buffer[i] = translate(item);
            }
            else
            {
                buffer.Add(translate(item));
            }
        }

        while (buffer.Count > 0)
        {
            Task<TResult> completed = await Task.WhenAny(buffer);
            buffer.Remove(completed);
            yield return await completed;
        }
    }
}
