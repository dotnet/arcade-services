// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using JetBrains.Annotations;

namespace BuildInsights.BuildAnalysis.Models;

public class BuildAnalysisFormatException : Exception
{
    public BuildAnalysisFormatException()
    {
    }

    public BuildAnalysisFormatException([CanBeNull] string message) : base(message)
    {
    }

    public BuildAnalysisFormatException([CanBeNull] string message, [CanBeNull] Exception innerException) : base(message, innerException)
    {
    }
}
