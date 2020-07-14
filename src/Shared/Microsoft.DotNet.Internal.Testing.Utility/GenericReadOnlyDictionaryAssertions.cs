// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using FluentAssertions.Collections;

namespace Microsoft.DotNet.Internal.Testing.Utility
{
    public class GenericReadOnlyDictionaryAssertions<TKey, TValue> : GenericDictionaryAssertions<TKey, TValue>
    {
        private class ReadOnlyDictionaryWrapper : IDictionary<TKey, TValue>
        {
            private readonly IReadOnlyDictionary<TKey, TValue> _backing;

            public ReadOnlyDictionaryWrapper(IReadOnlyDictionary<TKey, TValue> backing)
            {
                _backing = backing;
            }

            public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
            {
                return _backing.GetEnumerator();
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return ((IEnumerable) _backing).GetEnumerator();
            }

            public void Add(KeyValuePair<TKey, TValue> item)
            {
                throw new NotSupportedException();
            }

            public void Clear()
            {
                throw new NotSupportedException();
            }

            public bool Contains(KeyValuePair<TKey, TValue> item)
            {
                throw new NotSupportedException();
            }

            public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
            {
                throw new NotSupportedException();
            }

            public bool Remove(KeyValuePair<TKey, TValue> item)
            {
                throw new NotSupportedException();
            }

            public int Count => _backing.Count;
            public bool IsReadOnly => true;
            public void Add(TKey key, TValue value)
            {
                throw new NotSupportedException();
            }

            public bool ContainsKey(TKey key)
            {
                return _backing.ContainsKey(key);
            }

            public bool Remove(TKey key)
            {
                throw new NotSupportedException();
            }

            public bool TryGetValue(TKey key, out TValue value)
            {
                return _backing.TryGetValue(key, out value);
            }

            public TValue this[TKey key]
            {
                get => _backing[key];
                set => throw new NotSupportedException();
            }

            public ICollection<TKey> Keys => new ReadOnlyCollection<TKey>(_backing.Keys.ToList());
            public ICollection<TValue> Values => new ReadOnlyCollection<TValue>(_backing.Values.ToList());
        }

        public GenericReadOnlyDictionaryAssertions(IReadOnlyDictionary<TKey, TValue> dictionary) : base(new ReadOnlyDictionaryWrapper(dictionary))
        {
        }
    }
}
