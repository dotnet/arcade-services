// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.Darc;

namespace Maestro.MergePolicies;

public class DependencyUpdateSummary
{
    public string DependencyName { get; set; }

    public string FromVersion { get; set; }

    public string ToVersion { get; set; }

    public string FromCommitSha { get; set; }

    public string ToCommitSha { get; set; }

    public UnixPath RelativeBasePath { get; set; } = null;

    public DependencyUpdateSummary(DependencyUpdate du)
    {
        DependencyName = du.DependencyName;
        FromVersion = du.From?.Version;
        ToVersion = du.To?.Version;
        FromCommitSha = du.From?.Commit;
        ToCommitSha = du.To?.Commit;
    }

    public DependencyUpdateSummary() { }
}
