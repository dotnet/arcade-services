﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System;
using System.Runtime.Serialization;
using Maestro.Contracts;

namespace SubscriptionActorService.StateModel;

[DataContract]
public class UpdateAssetsParameters
{
    [DataMember]
    public Guid SubscriptionId { get; set; }

    [DataMember]
    public int BuildId { get; set; }

    [DataMember]
    public string SourceSha { get; set; }

    [DataMember]
    public string SourceRepo { get; set; }

    [DataMember]
    public List<Asset> Assets { get; set; }

    /// <summary>
    ///     If true, this is a coherency update and not driven by specific
    ///     subscription ids (e.g. could be multiple if driven by a batched subscription)
    /// </summary>
    [DataMember]
    public bool IsCoherencyUpdate { get; set; }

    /// <summary>
    ///     True for code-flow (VMR) subscriptions.
    /// </summary>
    [DataMember]
    public bool IsCodeFlow { get; set; }
}
