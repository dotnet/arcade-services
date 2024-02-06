// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.Metrics;

namespace ProductConstructionService.Api.Metrics;

public class JobMetricRecorderInstruments
{
    public JobMetricRecorderInstruments(Histogram<long> histogram, Counter<int> successCounter, Counter<int> failureCounter)
    {
        Histogram = histogram;
        SuccessCounter = successCounter;
        FailureCounter = failureCounter;
    }

    public Histogram<long> Histogram { get; }
    public Counter<int> SuccessCounter { get; }
    public Counter<int> FailureCounter { get; }
}
