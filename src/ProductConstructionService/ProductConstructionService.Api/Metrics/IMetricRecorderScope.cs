// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace ProductConstructionService.Api.Metrics;

public interface IMetricRecorderScope : IDisposable
{
    void SetSuccess();
}
