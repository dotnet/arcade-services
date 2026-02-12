// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;

namespace BuildInsights.BuildAnalysis.Models;

public class TestCaseResult
{
    public string Name { get; }
    public DateTimeOffset CompletedDate { get; }
    public TestOutcomeValue Outcome { get; }
    public int TestRunId { get; }
    public int Id { get; }
    public int Attempt { get; }
    public int BuildId { get; }
    public PreviousBuildRef FailingSince { get; }
    public string ErrorMessage { get; }
    public string StackTrace { get; }
    public string ProjectName { get; }
    public string BuildNumber { get; }
    public ResultGroupType ResultGroupType { get; }
    public ImmutableList<TestCaseResult> SubResults { get; }
    public int FailCount { get; }
    public int TotalCount { get; }
    public string Comment { get; }
    public double DurationInMilliseconds { get; }

    public TestCaseResult(
        string name,
        DateTimeOffset completedDate,
        TestOutcomeValue outcome,
        int testRunId,
        int id,
        int buildId,
        PreviousBuildRef failingSince,
        string errorMessage,
        string stackTrace,
        string projectName,
        string comment,
        double durationInMilliseconds,
        int attempt = default,
        ResultGroupType resultGroupType = default,
        ImmutableList<TestCaseResult> subResults = null,
        int failCount = 1,
        int totalCount = 1)
    {
        Name = name;
        CompletedDate = completedDate;
        Outcome = outcome;
        TestRunId = testRunId;
        Id = id;
        Attempt = attempt;
        BuildId = buildId;
        FailingSince = failingSince;
        ErrorMessage = errorMessage;
        StackTrace = stackTrace;
        ProjectName = projectName;
        Comment = comment;
        DurationInMilliseconds = durationInMilliseconds;
        ResultGroupType = resultGroupType;
        SubResults = subResults ?? ImmutableList<TestCaseResult>.Empty;
        FailCount = failCount;
        TotalCount = totalCount;
    }
}

public class PreviousBuildRef
{
    public string BuildNumber { get; }
    public DateTimeOffset Date { get; }

    public PreviousBuildRef(string buildNumber = null, DateTimeOffset date = default)
    {
        BuildNumber = buildNumber;
        Date = date;
    }
}

public class TimelineRecord
{
    public Guid Id { get; }
    public Guid? ParentId { get; }
    public TaskResult Result { get; }
    public ImmutableList<TimelineIssue> Issues { get; }
    public RecordType RecordType { get; }
    public int Attempt { get; }
    public string Name { get; } // Display Name
    public string Identifier { get; } // Name
    public string LogUrl { get; }
    public ImmutableList<TimelineAttempt> PreviousAttempts { get; }
    public int? Order { get; }
    public DateTimeOffset? StartTime { get; }

    public TimelineRecord(
        Guid id = default,
        Guid? parentId = default,
        TaskResult result = default,
        ImmutableList<TimelineIssue> issues = null,
        RecordType recordType = default,
        int attempt = default,
        string name = null,
        string identifier = null,
        string logUrl = null,
        ImmutableList<TimelineAttempt> previousAttempts = null,
        int? order = null,
        DateTimeOffset? startDate = null)
    {
        Id = id;
        ParentId = parentId;
        Result = result;
        Issues = issues ?? ImmutableList<TimelineIssue>.Empty;
        RecordType = recordType;
        Attempt = attempt;
        Name = name;
        Identifier = identifier;
        LogUrl = logUrl;
        PreviousAttempts = previousAttempts ?? ImmutableList<TimelineAttempt>.Empty;
        Order = order;
        StartTime = startDate;
    }
}

public class TimelineAttempt
{
    public int Attempt { get; }
    public Guid TimelineId { get; }

    public TimelineAttempt(int attempt, Guid timelineId)
    {
        Attempt = attempt;
        TimelineId = timelineId;
    }
}

public enum TaskResult
{
    None,
    Failed,
    Succeeded,
    SucceededWithIssues,
    Canceled,
    Skipped,
    Abandoned
}

public enum IssueType
{
    Error,
    Warning
}

public class TimelineIssue
{
    public string Message { get; }
    public IssueType Type { get; }
    public ImmutableDictionary<string,string> Data { get; }

    public TimelineIssue(string message, IssueType type = IssueType.Error, ImmutableDictionary<string, string> data = null)
    {
        Message = message;
        Type = type;
        Data = data ?? ImmutableDictionary<string, string>.Empty;
    }
}
