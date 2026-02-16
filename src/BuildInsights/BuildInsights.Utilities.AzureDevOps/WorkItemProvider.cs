// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using Microsoft.VisualStudio.Services.WebApi.Patch;
using Microsoft.VisualStudio.Services.WebApi.Patch.Json;

namespace BuildInsights.Utilities.AzureDevOps;

public interface IWorkItemService
{
    Task<WorkItem> GetWorkItemAsync(string orgId, string projectId, int workItemId, List<string> fieldNames);
    Task<WorkItem> CreateWorkItemAsync(string orgId, string projectId, string workItemType, string title, string description, string contact);
    Task<WorkItem> UpdateWorkItemAsync(string orgId, string projectId, int issueId, string description);
}

public sealed class WorkItemProvider : IWorkItemService
{
    private readonly VssConnectionProvider _connections;

    public WorkItemProvider(VssConnectionProvider connections)
    {
        _connections = connections;
    }

    public async Task<WorkItem> GetWorkItemAsync(
        string orgId,
        string projectId,
        int workItemId,
        List<string> fieldNames
    )
    {
        using var connection = _connections.GetConnection(orgId);
        WorkItemTrackingHttpClient workItemClient = connection.GetClient<WorkItemTrackingHttpClient>();

        return await workItemClient.GetWorkItemAsync(projectId, workItemId, fieldNames);
    }

    public async Task<WorkItem> CreateWorkItemAsync(
        string orgId,
        string projectId,
        string workItemType,
        string title,
        string description,
        string? contact = null)
    {
        JsonPatchDocument patch =
        [
            new JsonPatchOperation()
            {
                Operation = Operation.Add,
                Path = "/fields/System.Title",
                From = null,
                Value = title
            },
            new JsonPatchOperation()
            {
                Operation = Operation.Add,
                Path = "/fields/System.Description",
                From = null,
                Value = description
            },
            new JsonPatchOperation()
            {
                Operation = Operation.Add,
                Path = "/fields/System.AreaPath",
                From = null,
                Value = "internal\\Dotnet-Core-Engineering"
            },
        ];

        if (!string.IsNullOrEmpty(contact))
        {
            patch.Add(new JsonPatchOperation()
            {
                Operation = Operation.Add,
                Path = "/fields/Custom.Contact",
                From = null,
                Value = contact
            });
        }

        using var connection = _connections.GetConnection(orgId);
        WorkItemTrackingHttpClient workItemClient = connection.GetClient<WorkItemTrackingHttpClient>();

        return await workItemClient.CreateWorkItemAsync(patch, projectId, workItemType);
    }

    public async Task<WorkItem> UpdateWorkItemAsync(string orgId, string projectId, int issueId, string description)
    {
        JsonPatchDocument patch =
        [
            new JsonPatchOperation()
            {
                Operation = Operation.Add,
                Path = "/fields/System.Description",
                From = null,
                Value = description.ToString()
            },
        ];

        return await UpdateWorkItemAsync(orgId, projectId, issueId, patch);
    }

    private async Task<WorkItem> UpdateWorkItemAsync(string orgId, string projectId, int issueId, JsonPatchDocument patch)
    {
        using var connection = _connections.GetConnection(orgId);
        WorkItemTrackingHttpClient workItemClient = connection.GetClient<WorkItemTrackingHttpClient>();

        return await workItemClient.UpdateWorkItemAsync(patch, projectId, issueId);
    }
}
