// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using FluentAssertions;
using ProductConstructionService.Api.Queue;

namespace ProductConstructionService.Api.Tests;

public class JobProcessorStatusTests
{
    private ManualResetEventSlim _manualResetEventSlim;
    private JobsProcessorStatus _status;
    private readonly CancellationTokenSource _tokenSource;
    // To fully test the flow, we have to simulate the processor finishing a job and calling the WaitIfStopping method
    // We'll pass an already cancelled token to make sure we don't block the test thread
    private readonly Action _waitIfStoppingAction;

    // We only need this to satisfy the nullability check. They'll get overridden in the setup
    public JobProcessorStatusTests()
    {
        _manualResetEventSlim = new();
        _status = new(_manualResetEventSlim);
        _tokenSource = new();
        _tokenSource.Cancel(false);
        _waitIfStoppingAction = () => _status.WaitIfStopping(_tokenSource.Token);
    }

    [SetUp]
    public void Setup()
    {
        _manualResetEventSlim = new(true);
        _status = new(_manualResetEventSlim);
    }

    [Test]
    public void JobsProcessorStatusNormalFlow()
    {
        // The Processor starts in the Working state
        _manualResetEventSlim.IsSet.Should().BeTrue();
        _status.State.Should().Be(JobsProcessorState.Working);

        // Tell the Processor to finish it's current job and stop working
        _status.FinishJobAndStop();

        _manualResetEventSlim.IsSet.Should().BeFalse();
        _status.State.Should().Be(JobsProcessorState.FinishingJobAndStopping);

        // The Processor finished it's job, it should stop now
        _waitIfStoppingAction.Should().Throw<OperationCanceledException>();

        _manualResetEventSlim.IsSet.Should().BeFalse();
        _status.State.Should().Be(JobsProcessorState.StoppedWorking);

        // Start the Processor again
        _status.Start();

        _manualResetEventSlim.IsSet.Should().BeTrue();
        _status.State.Should().Be(JobsProcessorState.Working);
    }

    [Test]
    public void JobProcessorStatusMultipleStartStopFlow()
    {
        // The Processor starts in the Working state
        _manualResetEventSlim.IsSet.Should().BeTrue();
        _status.State.Should().Be(JobsProcessorState.Working);

        // Startup the processor again, it shouldn't do anything
        _status.Start();

        _manualResetEventSlim.IsSet.Should().BeTrue();
        _status.State.Should().Be(JobsProcessorState.Working);

        // Tell the Processor to finish it's current job and stop working
        _status.FinishJobAndStop();

        _manualResetEventSlim.IsSet.Should().BeFalse();
        _status.State.Should().Be(JobsProcessorState.FinishingJobAndStopping);

        // The Processor finished it's job, it should stop now
        _waitIfStoppingAction.Should().Throw<OperationCanceledException>();

        _manualResetEventSlim.IsSet.Should().BeFalse();
        _status.State.Should().Be(JobsProcessorState.StoppedWorking);
    }

    [Test]
    public void JobProcessorStatusStopStartBeforeFinishingItemFlow()
    {
        // The Processor starts in the Working state
        _manualResetEventSlim.IsSet.Should().BeTrue();
        _status.State.Should().Be(JobsProcessorState.Working);

        // Tell the Processor to finish it's current job and stop working
        _status.FinishJobAndStop();

        _manualResetEventSlim.IsSet.Should().BeFalse();
        _status.State.Should().Be(JobsProcessorState.FinishingJobAndStopping);

        // Tell the Processor again, before finishing the current job
        _status.Start();

        _manualResetEventSlim.IsSet.Should().BeTrue();
        _status.State.Should().Be(JobsProcessorState.Working);
    }

    [Test]
    public void JobProcessorStatusStopStopAfterFinishingJob()
    {
        // The Processor starts in the Working state
        _manualResetEventSlim.IsSet.Should().BeTrue();
        _status.State.Should().Be(JobsProcessorState.Working);

        // Tell the Processor to finish it's current job and stop working
        _status.FinishJobAndStop();

        _manualResetEventSlim.IsSet.Should().BeFalse();
        _status.State.Should().Be(JobsProcessorState.FinishingJobAndStopping);

        // The Processor finished it's job, it should stop now
        _waitIfStoppingAction.Should().Throw<OperationCanceledException>();

        _manualResetEventSlim.IsSet.Should().BeFalse();
        _status.State.Should().Be(JobsProcessorState.StoppedWorking);

        // Tell the Processor to stop, however, it's already stopped
        _status.FinishJobAndStop();

        _manualResetEventSlim.IsSet.Should().BeFalse();
        _status.State.Should().Be(JobsProcessorState.StoppedWorking);
    }
}
