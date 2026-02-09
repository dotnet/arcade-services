// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Reactive.Linq;

namespace BuildInsights.Utilities.Sql;

internal static class AsyncEnumerableExtensions
{
    public static IAsyncEnumerable<T> Create<T>(Func<Action<T>, Task> generator)
    {
        return Observable.Create<T>(async observer =>
        {
            try
            {
                await generator(observer.OnNext);
                observer.OnCompleted();
            }
            catch (Exception ex)
            {
                observer.OnError(ex);
            }
        }).ToAsyncEnumerable();
    }

    public static async IAsyncEnumerable<List<T>> BatchAsync<T>(IAsyncEnumerable<T> source, int batchSize)
    {
        var batch = new List<T>(batchSize);

        await foreach (var item in source)
        {
            batch.Add(item);
            if (batch.Count == batchSize)
            {
                yield return batch;
                batch = new List<T>(batchSize);
            }
        }

        if (batch.Count > 0)
        {
            yield return batch;
        }
    }
}
