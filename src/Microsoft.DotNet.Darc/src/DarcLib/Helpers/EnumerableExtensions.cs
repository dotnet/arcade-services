// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.DotNet.DarcLib.Helpers
{
    internal static class EnumerableExtensions
    {
        public static IEnumerable<T> NullAsEmpty<T>(this IEnumerable<T> source)
        {
            return source ?? Enumerable.Empty<T>();
        }

        public static TValue GetOrDefault<TKey, TValue>(
            this IDictionary<TKey, TValue> source,
            TKey key)
        {
            return source.TryGetValue(key, out var value)
                ? value
                : default;
        }

        public static TValue GetOrCreate<TKey, TValue>(
            this IDictionary<TKey, TValue> source,
            TKey key,
            Func<TValue> create)
        {
            return source.TryGetValue(key, out var value)
                ? value
                : source[key] = create();
        }
    }
}
