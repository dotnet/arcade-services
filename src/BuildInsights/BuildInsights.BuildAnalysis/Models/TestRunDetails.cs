// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;

namespace BuildInsights.BuildAnalysis.Models;

public class TestRunDetails
{
    public ImmutableList<TestCaseResult> Results { get; }

    public ImmutableList<HelixMetadata> Attachments { get; }

    public TestRunDetails(TestRunSummary run, ImmutableList<TestCaseResult> results, DateTimeOffset completedDate)
    {
        Id = run.Id;
        Name = run.Name;
        PipelineReference = run.PipelineReference;
        Results = results;
        CompletedDate = completedDate;
    }

    public int Id { get; }
    public string Name { get; }
    public PipelineReference PipelineReference { get; }
    public DateTimeOffset CompletedDate { get; }

    public TestRunDetails(TestRunSummary run, IEnumerable<TestCaseResult> results, DateTimeOffset completedDate)
        : this(run, results.ToImmutableList(), completedDate)
    {
    }

    public TestRunDetails(TestRunSummary run, IEnumerable<TestCaseResult> results, IEnumerable<HelixMetadata> metadata, DateTimeOffset completedDate)
        : this(run, results.ToImmutableList(), completedDate)
    {
        Attachments = metadata.ToImmutableList();
    }
}

public class TestRunSummary
{
    public TestRunSummary(int id, string name, PipelineReference pipelineReference)
    {
        Id = id;
        Name = name;
        PipelineReference = pipelineReference;
    }

    public int Id { get; }
    public string Name { get; }
    public PipelineReference PipelineReference { get; }
    public DateTimeOffset CompletedDate { get; }

    public TestRunSummary(int id, string name, PipelineReference pipelineReference, DateTimeOffset completedDate)
        : this(id, name, pipelineReference)
    {
        CompletedDate = completedDate;
    }
}
