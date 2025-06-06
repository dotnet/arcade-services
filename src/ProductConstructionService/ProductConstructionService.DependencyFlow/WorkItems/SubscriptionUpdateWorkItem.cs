﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.Serialization;
using ProductConstructionService.DependencyFlow.Model;

#nullable disable
namespace ProductConstructionService.DependencyFlow.WorkItems;

[DataContract]
public class SubscriptionUpdateWorkItem : DependencyFlowWorkItem
{
    [DataMember]
    public Guid SubscriptionId { get; init; }

    [DataMember]
    public SubscriptionType SubscriptionType { get; init; }

    [DataMember]
    public int BuildId { get; init; }

    [DataMember]
    public string SourceSha { get; init; }

    [DataMember]
    public string SourceRepo { get; init; }

    /// <summary>
    ///     If true, this is a coherency update and not driven by specific
    ///     subscription ids (e.g. could be multiple if driven by a batched subscription)
    /// </summary>
    [DataMember]
    public bool IsCoherencyUpdate { get; init; }

    public string GetRepoAtCommitUri() =>
        SourceRepo.Contains("github.com")
            ? $"{SourceRepo}/tree/{SourceSha}"
            : $"{SourceRepo}?version=GC{SourceSha}";

    public string GetFileAtCommitUri(string filePath) =>
        SourceRepo.Contains("github.com")
            ? $"{SourceRepo}/blob/{SourceSha}/{filePath}"
            : $"{SourceRepo}?version=GC{SourceSha}&path={filePath}";
}
