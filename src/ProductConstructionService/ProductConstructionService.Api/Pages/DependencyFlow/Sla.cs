// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;

namespace ProductConstructionService.Api.Pages.DependencyFlow;

[DebuggerDisplay("{GetDebuggerDisplay(),nq}")]
public class Sla
{
    public int WarningUnconsumedBuildAge { get; set; }
    public int FailUnconsumedBuildAge { get; set; }

    private string GetDebuggerDisplay()
         => $"{nameof(Sla)}(Warn: {WarningUnconsumedBuildAge}, Fail: {FailUnconsumedBuildAge})";
}
