// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using BuildInsights.GitHubGraphQL.GitHubGraphQLAPI;

namespace BuildInsights.GitHubGraphQL;

public interface IGitHubGraphQLClient
{
    /// <summary>
    /// Gets the project denoted by projectNumber from an organization
    /// </summary>
    /// <param name="organization">The github organization to search</param>
    /// <param name="projectNumber">The project number corresponding to the project we are interested in</param>
    /// <returns>The github projects (beta) project that corresponds to the projectNumber</returns>
    Task<GitHubGraphQLProjectV2> GetProjectForOrganization(string organization, int projectNumber);

    /// <summary>
    /// Get a field from a projects (beta) board
    /// </summary>
    /// <param name="field">The name of the field that we want to get the information of</param>
    /// <param name="projectId">The id of the project</param>
    /// <returns>The github projects (beta) field</returns>
    Task<GitHubGraphQLField> GetField(string field, string projectId);

    /// <summary>
    /// Get all issues from a projects (beta) board
    /// </summary>
    /// <param name="organization">The organization</param>
    /// <param name="projectNumber">The number of the project</param>
    /// <returns>A list of issues that are on the project board</returns>
    Task<List<GitHubGraphQLProjectV2Item>> GetAllProjectIssues(string organization, int projectNumber);

    /// <summary>
    /// Add an issue to a projects (beta) board or gets it if it is already on the board
    /// </summary>
    /// <param name="projectId">The id of the project</param>
    /// <param name="issueNodeId">The node id of the issue we want to add</param>
    /// <returns>The projectItem of the issue in the project board</returns>
    Task<GitHubGraphQLProjectV2Item> AddOrGetProjectIssue(string projectId, string issueNodeId);

    /// <summary>
    /// Update a text field of an issue in a projects (beta) board
    /// </summary>
    /// <param name="projectId">The id of the project</param>
    /// <param name="issueNodeId">The node id of the issue we want to update on the board</param>
    /// <param name="fieldId">The id of the field in the board that we want to change for the issue</param>
    /// <param name="textValue">The value to change the field to</param>
    Task UpdateIssueTextField(string projectId, string issueNodeId, string fieldId, string textValue);

    /// <summary>
    /// Update a single select field of an issue in a projects (beta) board
    /// </summary>
    /// <param name="projectId">The id of the project</param>
    /// <param name="issueNodeId">The node id of the issue we want to update on the board</param>
    /// <param name="fieldId">The id of the field in the board that we want to change for the issue</param>
    /// <param name="optionId">The id of the option that we want to change the field to</param>
    Task UpdateIssueSingleSelectField(string projectId, string issueNodeId, string fieldId, string optionId);

    /// <summary>
    /// Get a project (beta) item from an issue
    /// </summary>
    /// <param name="repoName">The repo name</param>
    /// <param name="repoOwner">The repo owner</param>
    /// <param name="issueNumber">The issue number.</param>
    /// <param name="projectId">The id of the project</param>
    /// <returns>The project V2 item id of the issue in the project board</returns>
    Task<string> TryGetIssueProjectItem(string repoName, string repoOwner, long issueNumber, string projectId);

    /// <summary>
    /// Delete an issue from a projects (beta) board
    /// </summary>
    /// <param name="projectId">The id of the project</param>
    /// <param name="projectItemId">The project item id of the issue from which we want to remove the project</param>
    Task DeleteProjectIssue(string projectId, string projectItemId);

}
