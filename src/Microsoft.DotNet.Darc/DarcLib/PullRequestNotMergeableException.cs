// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Microsoft.DotNet.DarcLib;

[Serializable]
public class PullRequestNotMergeableException : DarcException
{
    public PullRequestNotMergeableException() : base() { }

    public PullRequestNotMergeableException(string message) : base(message) { }
}
