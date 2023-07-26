// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.DotNet.DarcLib.Models.Darc;
public class DuplicatedAsset
{
    public long OriginalSize { get; set; }
    public long TotalSize { get; set; }
    public int TotalCopies { get; set; } = 0;
    public List<(string location, string optionalSubAssetPath)> Locations { get; set; } = new List<(string location, string optionalSubAssetPath)>();
}
