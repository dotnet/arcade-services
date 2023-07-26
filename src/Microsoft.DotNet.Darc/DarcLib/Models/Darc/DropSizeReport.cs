// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.DotNet.DarcLib.Models.Darc;
public class DropSizeReport
{
    public long DownloadSize { get; set; }
    public long SizeOnDisk { get; set; }
    public long SizeOfDuplicatedFilesBeforeUnpack { get; set; }
    public long SizeOfDuplicatedFilesAfterUnpack { get; set; }
    public List<DuplicatedAsset> DuplicatedAssets { get; set; } = new List<DuplicatedAsset>();
}
