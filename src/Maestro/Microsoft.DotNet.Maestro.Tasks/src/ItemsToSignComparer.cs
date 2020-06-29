using System;
using System.Collections.Generic;

namespace Microsoft.DotNet.Maestro.Tasks
{
    /// <summary>
    ///     Compares ItemsToSign objects based on file
    /// </summary>
    public class ItemsToSignComparer : IEqualityComparer<ItemsToSign>
    {
        public bool Equals(ItemsToSign x, ItemsToSign y)
        {
            return x.File.Equals(y.File, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode(ItemsToSign obj)
        {
            return (obj.File).GetHashCode();
        }
    }
}
