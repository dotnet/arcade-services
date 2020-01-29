// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

namespace DarcBot
{
    internal class TriageItem
    {
        public DateTime ModifiedDateTime { get; set; }
        public int BuildId { get; set; }
        public Guid RecordId { get; set; }
        public int Index { get; set; }
        public string UpdatedCategory { get; set; }
        public string Url { get; set; }

        public override bool Equals(object obj)
        {
            TriageItem compareItem = obj as TriageItem;
            return (!Object.ReferenceEquals(null, compareItem)) &&
                   (BuildId == compareItem.BuildId) &&
                   (RecordId == compareItem.RecordId) &&
                   (Index == compareItem.Index);
        }

        public override int GetHashCode() => new { BuildId, RecordId, Index }.GetHashCode();
    }
}
