// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.Metrics;

namespace ProductConstructionService.Api.Metrics;

public class JobMetricRecorderInstruments(Histogram<long> histogram, Counter<int> successCounter, Counter<int> failureCounter)
{
    private Histogram<long> _histogram { get; } = histogram;
    private Counter<int> _successCounter { get; } = successCounter;
    private Counter<int> _failureCounter { get; } = failureCounter;

    public void Record(long duration, bool success)
    {
        _histogram.Record(duration);
        if (success)
        {
            _successCounter.Add(1);
        }
        else
        {
            _failureCounter.Add(1);
        }
    }   
}

