// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using FluentAssertions.Collections;

namespace Microsoft.DotNet.Internal.Testing.Utility
{
    public static class ReadOnlyDictionaryShouldExtensions
    {
        public static GenericReadOnlyDictionaryAssertions<TKey, TValue> Should<TKey, TValue>(this IReadOnlyDictionary<TKey, TValue> dict)
        {
            return new GenericReadOnlyDictionaryAssertions<TKey, TValue>(dict);
        }

        public static GenericDictionaryAssertions<TKey, TValue> Should<TKey, TValue>(this Dictionary<TKey, TValue> dict)
        {
            return new GenericDictionaryAssertions<TKey, TValue>(dict);
        }
    }
}
