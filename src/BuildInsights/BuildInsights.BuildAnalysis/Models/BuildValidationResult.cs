// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace BuildInsights.BuildAnalysis.Models;

public class BuildValidationResult
{
    public BuildValidationStatus Result { get; }
    public string Message { get; }

    public BuildValidationResult(BuildValidationStatus result, string message)
    {
        Result = result;
        Message = message;
    }
}
