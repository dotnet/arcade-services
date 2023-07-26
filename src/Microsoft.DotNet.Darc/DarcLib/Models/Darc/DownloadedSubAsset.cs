// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.DotNet.DarcLib.Models.Darc;
public class DownloadedSubAsset
{
    public string RelativePath { get; set; }
    public string FileHash { get; set; }
    public long SizeInBytes { get; set; }
}
