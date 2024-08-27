// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.Serialization;
using ProductConstructionService.WorkItems;

#nullable disable
namespace ProductConstructionService.DependencyFlow.WorkItems;

[DataContract]
public abstract class ActorWorkItem : WorkItem
{
    [DataMember]
    public string ActorId { get; set; }
}
