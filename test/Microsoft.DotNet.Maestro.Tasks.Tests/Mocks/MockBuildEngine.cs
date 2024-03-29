// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Build.Framework;
using System;
using System.Collections;
using System.Collections.Generic;

namespace Microsoft.DotNet.Maestro.Tasks.Tests.Mocks;

public class MockBuildEngine : IBuildEngine
{
    public bool ContinueOnError => throw new NotImplementedException();

    public int LineNumberOfTaskNode => 0;

    public int ColumnNumberOfTaskNode => 0;

    public string ProjectFileOfTaskNode => "Fake File";

    public List<CustomBuildEventArgs> CustomBuildEvents = [];
    public List<BuildErrorEventArgs> BuildErrorEvents = [];
    public List<BuildMessageEventArgs> BuildMessageEvents = [];
    public List<BuildWarningEventArgs> BuildWarningEvents = [];

    public bool BuildProjectFile(string projectFileName, string[] targetNames, IDictionary globalProperties, IDictionary targetOutputs)
    {
        throw new NotImplementedException();
    }

    public void LogCustomEvent(CustomBuildEventArgs e)
    {
        CustomBuildEvents.Add(e);
    }

    public void LogErrorEvent(BuildErrorEventArgs e)
    {
        BuildErrorEvents.Add(e);
    }

    public void LogMessageEvent(BuildMessageEventArgs e)
    {
        BuildMessageEvents.Add(e);
    }

    public void LogWarningEvent(BuildWarningEventArgs e)
    {
        BuildWarningEvents.Add(e);
    }
}
