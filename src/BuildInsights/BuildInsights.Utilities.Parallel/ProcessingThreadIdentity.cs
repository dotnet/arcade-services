// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace BuildInsights.Utilities.Parallel;

public class ProcessingThreadIdentity
{
    public string Id { get; set; }

    public void Initialize(string id)
    {
        Id = id;
    }
}