// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

namespace DotNet.Status.Web
{
    internal static class ArrayExtension
    {
        internal static void Deconstruct<T>(this T[] array, out T item1, out T item2)
        {
            if (array == null)
            {
                throw new ArgumentNullException(nameof(array));
            }

            if (array.Length != 2)
            {
                throw new ArgumentException("Array is not correct length", nameof(array));
            }

            item1 = array[0];
            item2 = array[1];
        }
    }
}
