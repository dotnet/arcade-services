// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.Serialization;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.Darc;

namespace Maestro.MergePolicies;

[DataContract]
public class DependencyUpdateSummary
{
    [DataMember]
    public string DependencyName { get; set; }

    [DataMember]
    public string FromVersion { get; set; }

    [DataMember]
    public string ToVersion { get; set; }

    [DataMember]
    public string FromCommitSha { get; set; }

    [DataMember]
    public string ToCommitSha { get; set; }

    [DataMember]
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
