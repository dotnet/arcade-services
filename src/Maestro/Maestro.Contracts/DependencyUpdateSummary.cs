// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.Serialization;

namespace Maestro.Contracts;

[DataContract]
public class DependencyUpdateSummary
{
    [DataMember]
    public string DependencyName { get; set; }

    [DataMember]
    public string FromVersion { get; set; }

    [DataMember]
    public string ToVersion { get; set; }
}
