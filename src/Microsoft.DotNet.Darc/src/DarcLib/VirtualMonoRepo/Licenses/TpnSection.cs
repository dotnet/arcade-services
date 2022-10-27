// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo.Licenses;

public class TpnSection
{
    public static readonly ByHeaderNameComparer SectionComparer = new();

    public TpnSectionHeader Header { get; set; }
    public string Content { get; set; }

    public TpnSection(TpnSectionHeader header, string content)
    {
        Header = header;
        Content = content;
    }

    public override string ToString()
        => Header
           + TpnDocument.LineSeparator
           + TpnDocument.LineSeparator
           + Content;

    public class ByHeaderNameComparer : EqualityComparer<TpnSection>
    {
        public override bool Equals(TpnSection? x, TpnSection? y) =>
            string.Equals(x?.Header.Name, y?.Header.Name, StringComparison.OrdinalIgnoreCase);

        public override int GetHashCode(TpnSection obj) => obj.Header.Name.GetHashCode();
    }
}
