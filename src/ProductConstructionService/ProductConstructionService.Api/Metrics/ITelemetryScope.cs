// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace ProductConstructionService.Api.Metrics;

public interface ITelemetryScope : IDisposable
{
    /// <summary>
    /// Marks the operation running in the scope as successful, always call this method before disposing the scope
    /// </summary>
    void SetSuccess();
}
