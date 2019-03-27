// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Threading.Tasks;

namespace Microsoft.DotNet.DarcLib
{
    public class ExponentialRetry
    {
        private static readonly Random Randomizer = new Random();

        private const int RetryCount = 15;

        private const double RetryBackOffFactor = 1.3;

        private static int GetRetryDelay(int attempt)
        {
            var factor = RetryBackOffFactor;
            var min = (int)(Math.Pow(factor, attempt) * 1000);
            var max = (int)(Math.Pow(factor, attempt + 1) * 1000);
            return Randomizer.Next(min, max);
        }

        /// <summary>
        ///     Perform an action with exponential retry.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="function"></param>
        /// <param name="logRetry"></param>
        /// <param name="isRetryable"></param>
        /// <returns></returns>
        public static async Task<T> RetryAsync<T>(Func<Task<T>> function, Action<Exception> logRetry, Func<Exception, bool> isRetryable)
        {
            var attempt = 0;
            var maxAttempt = RetryCount;
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
    }
}
