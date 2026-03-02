// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace BuildInsights.BuildAnalysis.Models;

public class BuildAnalysisFormatException : Exception
{
    public BuildAnalysisFormatException()
    {
    }

    public BuildAnalysisFormatException(string message) : base(message)
    {
    }

    public BuildAnalysisFormatException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
