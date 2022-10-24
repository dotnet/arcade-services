// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.Internal.AzureDevOps;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.DotNet.AzureDevOpsTimeline.Tests;

public class TimelineBuilder
{
    private Timeline timeline;
    private List<TimelineRecord> records = new List<TimelineRecord>();

    public Timeline Build()
    {
        timeline.Records = records.ToArray();
        return timeline;
    }

    /// <summary>
    /// Create a builder starting with a simple default Timeline with no Records
    /// </summary>
    /// <param name="id">The timeline ID</param>
    public TimelineBuilder(string id, DateTimeOffset lastChangedOn)
    {
        timeline = new Timeline()
        {
            Id = id,
            LastChangedOn = lastChangedOn,
            Records = Array.Empty<TimelineRecord>()
        };
    }

    /// <summary>
    /// Create a builder starting with a simple default Timeline with no Records
    /// </summary>
    /// <param name="id">The timeline ID</param>
    public static TimelineBuilder EmptyTimeline(string id, DateTimeOffset lastChangedOn)
    {
        return new TimelineBuilder(id, lastChangedOn);
    }

    public TimelineBuilder AddRecord()
    {
        return AddRecord(null);
    }

    public TimelineBuilder AddRecord(string previousAttemptTimelineId)
    {
        int nextId = (records.Any() ? records.Max(r => int.Parse(r.Id)) : 0) + 1;

        TimelineRecord record = new TimelineRecord()
        {
            Id = nextId.ToString(),
            Issues = Array.Empty<TimelineIssue>()
        };

        if (string.IsNullOrEmpty(previousAttemptTimelineId))
        {
            record.PreviousAttempts = Array.Empty<TimelineAttempt>();
        }
        else
        {
            record.PreviousAttempts = new[] {
                new TimelineAttempt() { TimelineId = previousAttemptTimelineId }
            };
        }

        records.Add(record);

        return this;
    }

    public TimelineBuilder AddRecord(string workerName, string recordName, string logUrl)
    {
        int nextId =  (records.Any() ? records.Max(r => int.Parse(r.Id)) : 0) + 1;

        TimelineRecord record = new TimelineRecord()
        {
            Id = nextId.ToString(),
            Issues = Array.Empty<TimelineIssue>(),
            WorkerName = workerName,
            Log = new BuildLogReference()
            {
                Url = logUrl
            },
            PreviousAttempts = Array.Empty<TimelineAttempt>(),
            Name = recordName
        };

        records.Add(record);

        return this;
    }
}
