using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.DotNet.Services.Utility
{
    public class ExponentialRetry
    {
        private static readonly Random s_randomizer = new Random();

        private const int RetryCount = 10;

        private const double RetryBackOffFactor = 1.3;

        private static int GetRetryDelay(int attempt)
        {
            double factor = RetryBackOffFactor;
            int min = (int) (Math.Pow(factor, attempt) * 1000);
            int max = (int) (Math.Pow(factor, attempt + 1) * 1000);
            return s_randomizer.Next(min, max);
        }

        public static async Task<T> RetryAsync<T>(
            Func<Task<T>> function,
            Action<Exception> logRetry,
            Func<Exception, bool> isRetryable)
        {
            int attempt = 0;
            int maxAttempt = RetryCount;
            while (true)
            {
                try
                {
                    return await function();
                }
                catch (Exception ex) when (isRetryable(ex))
                {
                    if (attempt >= maxAttempt)
                    {
                        throw;
                    }

                    logRetry(ex);
                }

                await Task.Delay(GetRetryDelay(attempt));
                attempt++;
            }
        }

        public static async Task<T> RetryAsync<T>(
            Func<CancellationToken, Task<T>> function,
            Action<Exception> logRetry,
            Func<Exception, bool> isRetryable,
            CancellationToken cancellationToken)
        {
            int attempt = 0;
            int maxAttempt = RetryCount;
            while (true)
            {
                try
                {
                    return await function(cancellationToken);
                }
                catch (OperationCanceledException ex) when (ex.CancellationToken == cancellationToken)
                {
                    // It's cancelled, get out of here.
                    throw;
                }
                catch (Exception ex) when (isRetryable(ex))
                {
                    if (attempt >= maxAttempt)
                    {
                        throw;
                    }

                    logRetry(ex);
                }

                await Task.Delay(GetRetryDelay(attempt), cancellationToken);
                attempt++;
            }
        }
        
        
        public static async Task RetryAsync(
            Func<Task> function,
            Action<Exception> logRetry,
            Func<Exception, bool> isRetryable)
        {
            int attempt = 0;
            int maxAttempt = RetryCount;
            while (true)
            {
                try
                {
                    await function();
                    return;
                }
                catch (Exception ex) when (isRetryable(ex))
                {
                    if (attempt >= maxAttempt)
                    {
                        throw;
                    }

                    logRetry(ex);
                }

                await Task.Delay(GetRetryDelay(attempt));
                attempt++;
            }
        }

        public static async Task RetryAsync(
            Action function,
            Action<Exception> logRetry,
            Func<Exception, bool> isRetryable)
        {
            int attempt = 0;
            int maxAttempt = RetryCount;
            while (true)
            {
                try
                {
                    function();
                    return;
                }
                catch (Exception ex) when (isRetryable(ex))
                {
                    if (attempt >= maxAttempt)
                    {
                        throw;
                    }

                    logRetry(ex);
                }

                await Task.Delay(GetRetryDelay(attempt));
                attempt++;
            }
        }

        public static async Task RetryAsync(
            Func<CancellationToken, Task> function,
            Action<Exception> logRetry,
            Func<Exception, bool> isRetryable,
            CancellationToken cancellationToken)
        {
            int attempt = 0;
            int maxAttempt = RetryCount;
            while (true)
            {
                try
                {
                    await function(cancellationToken);
                    return;
                }
                catch (OperationCanceledException ex) when (ex.CancellationToken == cancellationToken)
                {
                    // It's cancelled, get out of here.
                    throw;
                }
                catch (Exception ex) when (isRetryable(ex))
                {
                    if (attempt >= maxAttempt)
                    {
                        throw;
                    }

                    logRetry(ex);
                }

                await Task.Delay(GetRetryDelay(attempt), cancellationToken);
                attempt++;
            }
        }
    }
}
