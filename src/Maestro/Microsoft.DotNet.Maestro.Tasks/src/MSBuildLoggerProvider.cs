// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Build.Utilities;
using Microsoft.Extensions.Logging;

namespace Microsoft.DotNet.Maestro.Tasks;

/// <summary>
/// An <see cref="ILoggerProvider"/> that adapts <see cref="ILogger"/> calls
/// (used by services resolved from the task's mini DI container) onto the
/// MSBuild <see cref="TaskLoggingHelper"/>.
/// </summary>
internal sealed class MSBuildLoggerProvider : ILoggerProvider
{
    private readonly TaskLoggingHelper _log;

    public MSBuildLoggerProvider(TaskLoggingHelper log)
    {
        _log = log;
    }

    public ILogger CreateLogger(string categoryName) => new MSBuildLogger(_log);

    public void Dispose()
    {
    }
}
