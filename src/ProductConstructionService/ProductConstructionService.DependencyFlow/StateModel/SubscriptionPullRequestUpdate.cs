// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.Serialization;

#nullable disable
namespace ProductConstructionService.DependencyFlow.StateModel;

[DataContract]
public class SubscriptionPullRequestUpdate
{
    [DataMember]
    public Guid SubscriptionId { get; set; }

    [DataMember]
    public int BuildId { get; set; }
}
