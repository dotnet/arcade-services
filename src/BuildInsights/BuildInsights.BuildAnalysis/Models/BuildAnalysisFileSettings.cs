// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace BuildInsights.BuildAnalysis.Models;

public class BuildAnalysisFileSettings
{
    public string Path { get; set; }
    public string FileName { get; set; }
    public string FilePath => string.Concat(Path, FileName);
}
