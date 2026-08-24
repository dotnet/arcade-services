// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.Metrics;
using AwesomeAssertions;
using Maestro.Common.Telemetry;
using Microsoft.ApplicationInsights;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ApplicationInsightsTelemetryConfiguration = Microsoft.ApplicationInsights.Extensibility.TelemetryConfiguration;

namespace Maestro.Common.Tests;

public class TelemetryRecorderTests
{
    [Test]
    public void AddTelemetryConfiguresMetricNamespace()
    {
        // Arrange
        const string metricNamespace = "Maestro.Common.Tests";
        ServiceCollection services = new();
        services.AddMetrics();
        services.AddTelemetry(options => options.MetricNamespace = metricNamespace);
        using ServiceProvider serviceProvider = services.BuildServiceProvider();
        List<RecordedMeasurement> measurements = [];
        using MeterListener listener = CreateListener(measurements, metricNamespace);

        // Act
        serviceProvider.GetRequiredService<IMetricRecorder>().QueueMessageReceived(10);

        // Assert
        RecordedMeasurement measurement = measurements.Should().ContainSingle().Subject;
        measurement.InstrumentName.Should().Be("queue.wait_time");
        measurement.Value.Should().Be(10);
    }

    [Test]
    public void RecordGitOperationRecordsCountAndDurationMetricsWithoutRepositoryDimension()
    {
        // Arrange
        using ServiceProvider serviceProvider = new ServiceCollection().AddMetrics().BuildServiceProvider();
        MetricRecorder metricRecorder = new(
            serviceProvider.GetRequiredService<IMeterFactory>(),
            Options.Create(new TelemetryOptions()));
        TelemetryRecorder telemetryRecorder = new(
            NullLogger<TelemetryRecorder>.Instance,
            new TelemetryClient(new ApplicationInsightsTelemetryConfiguration()),
            metricRecorder);
        List<RecordedMeasurement> measurements = [];
        using MeterListener listener = CreateListener(measurements);

        // Act
        using (ITelemetryScope scope = telemetryRecorder.RecordGitOperation(
            TrackedGitOperation.Fetch,
            "https://example.invalid/repository"))
        {
            scope.SetSuccess();
        }

        // Assert
        measurements.Should().ContainSingle(measurement =>
            measurement.InstrumentName == "telemetry.event.count"
            && measurement.Value == 1);
        measurements.Should().ContainSingle(measurement =>
            measurement.InstrumentName == "telemetry.event.duration"
            && measurement.Value >= 0);
        measurements.Should().AllSatisfy(measurement =>
        {
            measurement.Tags.Should().Contain("EventName", "GitFetch");
            measurement.Tags.Should().Contain("Success", bool.TrueString);
            measurement.Tags.Should().NotContainKey("Uri");
        });
    }

    [Test]
    public void RecordCustomEventRecordsCountMetric()
    {
        // Arrange
        using ServiceProvider serviceProvider = new ServiceCollection().AddMetrics().BuildServiceProvider();
        MetricRecorder metricRecorder = new(
            serviceProvider.GetRequiredService<IMeterFactory>(),
            Options.Create(new TelemetryOptions()));
        TelemetryRecorder telemetryRecorder = new(
            NullLogger<TelemetryRecorder>.Instance,
            new TelemetryClient(new ApplicationInsightsTelemetryConfiguration()),
            metricRecorder);
        List<RecordedMeasurement> measurements = [];
        using MeterListener listener = CreateListener(measurements);

        // Act
        telemetryRecorder.RecordCustomEvent(
            CustomEventType.PullRequestUpdateFailed,
            new Dictionary<string, string> { ["PullRequestUrl"] = "https://example.invalid/pull/1" });

        // Assert
        RecordedMeasurement measurement = measurements.Should().ContainSingle().Subject;
        measurement.InstrumentName.Should().Be("telemetry.event.count");
        measurement.Value.Should().Be(1);
        measurement.Tags.Should().Contain("EventName", "PullRequestUpdateFailed");
        measurement.Tags.Should().NotContainKey("PullRequestUrl");
    }

    private static MeterListener CreateListener(
        List<RecordedMeasurement> measurements,
        string metricNamespace = TelemetryOptions.DefaultMetricNamespace)
    {
        MeterListener listener = new();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == metricNamespace)
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<int>((instrument, value, tags, _) =>
            measurements.Add(new RecordedMeasurement(instrument.Name, value, CopyTags(tags))));
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            measurements.Add(new RecordedMeasurement(instrument.Name, value, CopyTags(tags))));
        listener.Start();
        return listener;
    }

    private static Dictionary<string, object?> CopyTags(
        ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        Dictionary<string, object?> copiedTags = [];
        foreach ((string name, object? value) in tags)
        {
            copiedTags.Add(name, value);
        }

        return copiedTags;
    }

    private sealed record RecordedMeasurement(
        string InstrumentName,
        double Value,
        Dictionary<string, object?> Tags);
}
