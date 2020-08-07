using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.DotNet.Services.Utility
{
    public class ExponentialRetryOptions
    {
        public int RetryCount { get; set; } = 10;
        public double RetryBackOffFactor{ get; set; } = 1.3;
    }

    public class ExponentialRetry
    {
        private readonly IOptions<ExponentialRetryOptions> _options;
        private static readonly Random s_randomizer = new Random();

        public static readonly ExponentialRetry Default = new ExponentialRetry(
            Options.Create(new ExponentialRetryOptions {RetryCount = 10, RetryBackOffFactor = 1.3})
        );

        public ExponentialRetry(IOptions<ExponentialRetryOptions> options)
        {
            _options = options;
        }

        private int GetRetryDelay(int attempt)
        {
            double factor = _options.Value.RetryBackOffFactor;
            int min = (int) (Math.Pow(factor, attempt) * 1000);
            int max = (int) (Math.Pow(factor, attempt + 1) * 1000);
            return s_randomizer.Next(min, max);
        }

        public async Task<T> RetryAsync<T>(
            Func<Task<T>> function,
            Action<Exception> logRetry,
            Func<Exception, bool> isRetryable)
        {
            int attempt = 0;
            int maxAttempt = _options.Value.RetryCount;
            while (true)
            {
                try
                {
                    return await function().ConfigureAwait(false);
                }
                catch (Exception ex) when (isRetryable(ex))
                {
                    if (attempt >= maxAttempt)
                    {
                        throw;
                    }

                    logRetry(ex);
                }

                await Task.Delay(GetRetryDelay(attempt)).ConfigureAwait(false);
                attempt++;
            }
        }

        public async Task<T> RetryAsync<T>(
            Func<CancellationToken, Task<T>> function,
            Action<Exception> logRetry,
            Func<Exception, bool> isRetryable,
            CancellationToken cancellationToken)
        {
            int attempt = 0;
            int maxAttempt = _options.Value.RetryCount;
            while (true)
            {
                try
                {
                    return await function(cancellationToken).ConfigureAwait(false);
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

                await Task.Delay(GetRetryDelay(attempt), cancellationToken).ConfigureAwait(false);
                attempt++;
            }
        }
        
        
        public async Task RetryAsync(
            Func<Task> function,
            Action<Exception> logRetry,
            Func<Exception, bool> isRetryable)
        {
            int attempt = 0;
            int maxAttempt = _options.Value.RetryCount;
            while (true)
            {
                try
                {
                    await function().ConfigureAwait(false);
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

                await Task.Delay(GetRetryDelay(attempt)).ConfigureAwait(false);
                attempt++;
            }
        }

        public async Task RetryAsync(
            Action function,
            Action<Exception> logRetry,
            Func<Exception, bool> isRetryable)
        {
            int attempt = 0;
            int maxAttempt = _options.Value.RetryCount;
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

                await Task.Delay(GetRetryDelay(attempt)).ConfigureAwait(false);
                attempt++;
            }
        }

        public async Task RetryAsync(
            Func<CancellationToken, Task> function,
            Action<Exception> logRetry,
            Func<Exception, bool> isRetryable,
            CancellationToken cancellationToken)
        {
            int attempt = 0;
            int maxAttempt = _options.Value.RetryCount;
            while (true)
            {
                try
                {
                    await function(cancellationToken).ConfigureAwait(false);
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

                await Task.Delay(GetRetryDelay(attempt), cancellationToken).ConfigureAwait(false);
                attempt++;
            }
        }
    }
}
