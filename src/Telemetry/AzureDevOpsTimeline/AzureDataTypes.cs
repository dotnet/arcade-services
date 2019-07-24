// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using Newtonsoft.Json;

namespace Microsoft.DotNet.AzureDevOpsTimeline
{
    /// <summary>
    ///     https://docs.microsoft.com/en-us/rest/api/azure/devops/build/builds/list?view=azure-devops-rest-5.0#build
    /// </summary>
    public sealed class Build
    {
        public BuildLinks Links { get; set; }
        public string BuildNumber { get; set; }
        public int BuildNumberRevision { get; set; }
        public BuildController BuildController { get; set; }
        public DefinitionReference Definition { get; set; }
        public bool Deleted { get; set; }
        public string DeletedDate { get; set; }
        public string DeletedReason { get; set; }
        public Demand[] Demands { get; set; }
        public string FinishTime { get; set; }
        public int Id { get; set; }
        public bool KeepForever { get; set; }
        public IdentityRef LastChangedBy { get; set; }
        public string LastChangedDate { get; set; }
        public BuildLogReference Logs { get; set; }
        public TaskOrchestrationPlanReference OrchestrationPlan { get; set; }
        public string Parameters { get; set; }
        public TaskOrchestrationPlanReference[] Plans { get; set; }
        public string Priority { get; set; }
        public TeamProjectReference Project { get; set; }
        public PropertiesCollection Properties { get; set; }
        public string Quality { get; set; }
        public AgentPoolQueue Queue { get; set; }
        public string QueueOptions { get; set; }
        public int QueuePosition { get; set; }
        public string QueueTime { get; set; }
        public string Reason { get; set; }
        public BuildRepository Repository { get; set; }
        public IdentityRef RequestedBy { get; set; }
        public IdentityRef RequestedFor { get; set; }
        public string Result { get; set; }
        public bool RetainedByRelease { get; set; }
        public string SourceBranch { get; set; }
        public string SourceVersion { get; set; }
        public string StartTime { get; set; }
        public string Status { get; set; }
        public string[] Tags { get; set; }
        public object TriggerInfo { get; set; }
        public Build TriggeredByBuild { get; set; }
        public string Uri { get; set; }
        public string Url { get; set; }
        public BuildRequestValidationResult[] ValidationResults { get; set; }

        public override string ToString()
        {
            return $"Id: {Id} BuildNumber: {BuildNumber}";
        }
    }

    public sealed class BuildLinks
    {
        public Badge Self { get; set; }
        public Badge Web { get; set; }
        public Badge SourceVersionDisplayUri { get; set; }
        public Badge Timeline { get; set; }
        public Badge Badge { get; set; }
    }

    public sealed class Badge
    {
        public Uri Href { get; set; }
    }

    /// <summary>
    ///     https://docs.microsoft.com/en-us/rest/api/azure/devops/build/builds/list?view=azure-devops-rest-5.0#buildstatus
    /// </summary>
    public sealed class BuildRequestValidationResult
    {
        public string Message { get; set; }
        public string Result { get; set; }
    }

    /// <summary>
    ///     https://docs.microsoft.com/en-us/rest/api/azure/devops/build/builds/list?view=azure-devops-rest-5.0#buildreason
    /// </summary>
    public sealed class BuildRepository
    {
        public string Id { get; set; }
        public string Name { get; set; }
    }

    /// <summary>
    ///     https://docs.microsoft.com/en-us/rest/api/azure/devops/build/builds/list?view=azure-devops-rest-5.0#agentpoolqueue
    /// </summary>
    public sealed class AgentPoolQueue
    {
        // TODO
    }

    /// <summary>
    ///     https://docs.microsoft.com/en-us/rest/api/azure/devops/build/builds/list?view=azure-devops-rest-5.0#propertiescollection
    /// </summary>
    public sealed class PropertiesCollection
    {
        // TODO
    }
    /// <summary>
    ///     https://docs.microsoft.com/en-us/rest/api/azure/devops/build/builds/list?view=azure-devops-rest-5.0#buildcontroller
    /// </summary>
    public sealed class BuildController
    {
        // TODO
    }

    /// <summary>
    ///     https://docs.microsoft.com/en-us/rest/api/azure/devops/build/builds/list?view=azure-devops-rest-5.0#definitionreference
    /// </summary>
    public sealed class DefinitionReference
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Path { get; set; }
    }

    /// <summary>
    ///     https://docs.microsoft.com/en-us/rest/api/azure/devops/build/builds/list?view=azure-devops-rest-5.0#identityref
    /// </summary>
    public sealed class IdentityRef
    {
        // TODO
    }

    /// <summary>
    ///     https://docs.microsoft.com/en-us/rest/api/azure/devops/build/builds/list?view=azure-devops-rest-5.0#taskorchestrationplanreference
    /// </summary>
    public sealed class TaskOrchestrationPlanReference
    {
        // TODO
    }

    /// <summary>
    ///     https://docs.microsoft.com/en-us/rest/api/azure/devops/build/builds/list?view=azure-devops-rest-5.0#validationresult
    /// </summary>
    public sealed class ValidationResult
    {
        public string Result { get; set; }
        public string Message { get; set; }
    }

    /// <summary>
    ///     https://docs.microsoft.com/en-us/rest/api/azure/devops/build/builds/list?view=azure-devops-rest-5.0#teamprojectreference
    /// </summary>
    public sealed class TeamProjectReference
    {
        public string Id { get; set; }
        public string Name { get; set; }
    }

    /// <summary>
    ///     https://docs.microsoft.com/en-us/rest/api/azure/devops/build/builds/list?view=azure-devops-rest-5.0#demand
    /// </summary>
    public sealed class Demand
    {
        public string Name { get; set; }
        public string Value { get; set; }
    }

    /// <summary>
    ///     https://docs.microsoft.com/en-us/rest/api/azure/devops/build/builds/get%20build%20logs?view=azure-devops-rest-5.0#buildlog
    /// </summary>
    public sealed class BuildLog
    {
        public int Id { get; set; }
        public int LineCount { get; set; }
        public string Type { get; set; }

        public override string ToString()
        {
            return $"Id: {Id} Type: {Type} LineCount: {LineCount}";
        }
    }

    /// <summary>
    ///     https://docs.microsoft.com/en-us/rest/api/azure/devops/build/timeline/get?view=azure-devops-rest-5.0#timeline
    /// </summary>
    public sealed class Timeline
    {
        public string Id { get; set; }
        public int ChangeId { get; set; }
        public string LastChangedBy { get; set; }
        public string LastChangedOn { get; set; }
        public TimelineRecord[] Records { get; set; }
        public string Url { get; set; }
    }

    /// <summary>
    ///     https://docs.microsoft.com/en-us/rest/api/azure/devops/build/timeline/get?view=azure-devops-rest-5.0#timelinerecord
    /// </summary>
    public sealed class TimelineRecord
    {
        public int Attempt { get; set; }
        public int ChangeId { get; set; }
        public string CurrentOperation { get; set; }
        public TimelineReference Details { get; set; }
        public int ErrorCount { get; set; }
        public string FinishTime { get; set; }
        public string Id { get; set; }
        public Issue[] Issues { get; set; }
        public string LastModified { get; set; }
        public string Name { get; set; }
        public BuildLogReference Log { get; set; }
        public int Order { get; set; }
        public string ParentId { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public int PercentComplete { get; set; }

        public TimelineAttempt[] PreviousAttempts { get; set; }
        public string Result { get; set; }
        public string ResultCode { get; set; }
        public string StartTime { get; set; }
        public TaskReference Task { get; set; }
        public string Url { get; set; }
        public int WarningCount { get; set; }
        public string WorkerName { get; set; }
    }

    /// <summary>
    ///     https://docs.microsoft.com/en-us/rest/api/azure/devops/build/timeline/get?view=azure-devops-rest-5.0#buildlogreference
    /// </summary>
    public sealed class BuildLogReference
    {
        public int Id { get; set; }
        public string Type { get; set; }
        public string Url { get; set; }
    }

    /// <summary>
    ///     https://docs.microsoft.com/en-us/rest/api/azure/devops/build/timeline/get?view=azure-devops-rest-5.0#issue
    /// </summary>
    public sealed class Issue
    {
        public string Category { get; set; }
        public object Data { get; set; }
        public string Message { get; set; }
        public string Type { get; set; }
    }

    /// <summary>
    ///     https://docs.microsoft.com/en-us/rest/api/azure/devops/build/timeline/get?view=azure-devops-rest-5.0#timelinereference
    /// </summary>
    public sealed class TimelineReference
    {
        public int ChangeId { get; set; }
        public string Id { get; set; }
        public string Url { get; set; }
    }

    /// <summary>
    ///     https://docs.microsoft.com/en-us/rest/api/azure/devops/build/timeline/get?view=azure-devops-rest-5.0#timelineattempt
    /// </summary>
    public sealed class TimelineAttempt
    {
        public int Attempt { get; set; }
        public string RecordId { get; set; }
        public string TimelineId { get; set; }
    }

    /// <summary>
    ///     https://docs.microsoft.com/en-us/rest/api/azure/devops/build/timeline/get?view=azure-devops-rest-5.0#taskreference
    /// </summary>
    public sealed class TaskReference
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Version { get; set; }
    }

    /// <summary>
    ///     https://docs.microsoft.com/en-us/rest/api/azure/devops/build/artifacts/get%20artifact?view=azure-devops-rest-5.0#buildartifact
    /// </summary>
    public sealed class BuildArtifact
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public ArtifactResource Resource { get; set; }
    }

    /// <summary>
    ///     https://docs.microsoft.com/en-us/rest/api/azure/devops/build/artifacts/get%20artifact?view=azure-devops-rest-5.0#artifactresource
    /// </summary>
    public sealed class ArtifactResource
    {
        public string Data { get; set; }
        public string DownloadUrl { get; set; }
        public object Properties { get; set; }
        public string Type { get; set; }

        /// <summary>
        ///     The full http link to the resource
        /// </summary>
        public string Url { get; set; }
    }
}
