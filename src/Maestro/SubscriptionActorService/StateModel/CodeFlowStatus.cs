// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.Serialization;

namespace SubscriptionActorService.StateModel;

[DataContract]
public class CodeFlowStatus
{
    [DataMember]
    public string PrBranch { get; set; }

    [DataMember]
    public string SourceSha { get; set; }
}
