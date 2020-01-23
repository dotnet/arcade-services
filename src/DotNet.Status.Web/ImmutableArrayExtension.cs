// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace DotNet.Status.Web
{
    internal static class ImmutableArrayExtension
    {
        internal static IEnumerable<T> OrEmpty<T>(this IEnumerable<T> array)
        {
            return array ?? Enumerable.Empty<T>();
        }

        internal static IEnumerable<T> OrEmpty<T>(this ImmutableArray<T> array)
        {
            return array.IsDefault ? Enumerable.Empty<T>() : array;
        }
    }
}
