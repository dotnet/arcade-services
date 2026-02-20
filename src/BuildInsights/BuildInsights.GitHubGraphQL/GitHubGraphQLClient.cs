// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text;
using BuildInsights.GitHubGraphQL.GitHubGraphQLAPI;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace BuildInsights.GitHubGraphQL;

public class GitHubGraphQLClient : IGitHubGraphQLClient
{
    private readonly ILogger<GitHubGraphQLClient> _logger;
    private readonly IGitHubGraphQLHttpClientFactory _clientFactory;

    public GitHubGraphQLClient(
        ILogger<GitHubGraphQLClient> logger,
        IGitHubGraphQLHttpClientFactory clientFactory)
    {
        _logger = logger;
        _clientFactory = clientFactory;
    }

    public async Task<GitHubGraphQLField> GetField(string field, string projectId)
    {
        string query = @$"query{{
                node(id: ""{projectId}"") {{
                    ... on ProjectV2 {{
                        fields(first:20) {{
                            nodes {{
                                ... on ProjectV2Field {{
                                    id
                                    name
                                }}
                                ... on ProjectV2SingleSelectField {{
                                    id
                                    name
                                    options {{
                                        id
                                        name
                                    }}
                                }}
                            }}
                        }}
                    }}
                }}
            }}";

        GitHubGraphQLResponse responseObj = await SendGitHubGraphQLRequestAsync(query);
        return responseObj.Data.Node.Fields.Nodes.FirstOrDefault(f => f.Name.Equals(field, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<List<GitHubGraphQLProjectV2Item>> GetAllProjectIssues(string organization, int projectNumber)
    {
        string query = GetProjectIssuesQuery(organization, projectNumber);
        GitHubGraphQLResponse responseObj = await SendGitHubGraphQLRequestAsync(query);
        List<GitHubGraphQLProjectV2Item> allIssues = responseObj.Data.Organization.ProjectV2.Items.Nodes.ToList();

        while(responseObj.Data.Organization.ProjectV2.Items.PageInfo.HasNextPage)
        {
            query = GetProjectIssuesQuery(organization, projectNumber, responseObj.Data.Organization.ProjectV2.Items.PageInfo.EndCursor);
            responseObj = await SendGitHubGraphQLRequestAsync(query);

            allIssues.AddRange(responseObj.Data.Organization.ProjectV2.Items.Nodes);
        }
        return allIssues;
    }

    public async Task<GitHubGraphQLProjectV2> GetProjectForOrganization(string organization, int projectNumber)
    {
        string query = @$"query{{
                organization(login: ""{organization}"") {{
                    projectV2(number: {projectNumber}) {{
                        id title
                    }}
                }}
            }}";

        GitHubGraphQLResponse responseObj = await SendGitHubGraphQLRequestAsync(query);
        return responseObj.Data.Organization.ProjectV2;
    }

    public async Task<string> TryGetIssueProjectItem(string repoName, string repoOwner, long issueNumber, string projectId)
    {
        string query = @$"query{{
                repository(name: ""{repoName}"", owner: ""{repoOwner}"") {{
                    issue(number: {issueNumber}) {{
                        id
                        projectsV2 (first: 20) {{
                            nodes {{
                                id
                            }}
                        }}
                    }}
                }}
            }}";

        GitHubGraphQLResponse responseObj = await SendGitHubGraphQLRequestAsync(query);
        GitHubGraphQLProjectV2 project = responseObj.Data.Repository.Issue.ProjectsV2.Nodes.FirstOrDefault(i => i.Id == projectId);

        if (project != null ) {
            var item = await AddOrGetProjectIssue(projectId, responseObj.Data.Repository.Issue.Id);
            return item.Id;
        }

        return null;
    }

    public async Task<GitHubGraphQLProjectV2Item> AddOrGetProjectIssue(string projectId, string issueNodeId)
    {
        string query = @$"mutation {{
                addProjectV2ItemById(input: {{
                    projectId: ""{projectId}"" 
                    contentId: ""{issueNodeId}""
                }}) {{
                    item {{
                        id
                        isArchived
                    }}
                }}
            }}";

        GitHubGraphQLResponse responseObj = await SendGitHubGraphQLRequestAsync(query);

        try
        {
            // returns true if item is added or already exists
            if (responseObj.Data.AddProjectV2ItemById.Item == null)
            {
                throw new Exception($"Failed to add item {issueNodeId} to project {projectId}");
            }
        }
        catch (NullReferenceException e)
        {
            string firstNullField = "(none)";

            if (responseObj is null)
                firstNullField = nameof(responseObj);
            else if (responseObj.Data is null)
                firstNullField = nameof(responseObj.Data);
            else if (responseObj.Data.UpdateProjectNextV2Field is null)
                firstNullField = nameof(responseObj.Data.UpdateProjectNextV2Field);
            else if (responseObj.Data.UpdateProjectNextV2Field.ProjectV2Item is null)
                firstNullField = nameof(responseObj.Data.UpdateProjectNextV2Field.ProjectV2Item);

            _logger.LogError(e, "Unexpected null field \"{NullFieldName}\"", firstNullField);
            throw;
        }

        return responseObj.Data.AddProjectV2ItemById.Item;
    }

    public async Task DeleteProjectIssue(string projectId, string projectItemId)
    {
        string query = @$"mutation {{
                deleteProjectV2Item(input: {{
                    projectId: ""{projectId}"" 
                    itemId: ""{projectItemId}""
                }}) {{
                   deletedItemId
                }}
            }}";

        GitHubGraphQLResponse responseObj = await SendGitHubGraphQLRequestAsync(query);

        if (responseObj.Data.DeleteProjectV2Item.DeletedItemId == null)
        {
            throw new Exception("Failed to delete item from project");
        }
    }

    public async Task UpdateIssueTextField (string projectId, string issueNodeId, string fieldId, string textValue)
    {
        string value = @$"text: ""{textValue}""";
        await UpdateIssueField(projectId, issueNodeId, fieldId, value);
    }

    public async Task UpdateIssueSingleSelectField(string projectId, string issueNodeId, string fieldId, string optionId)
    {
        string value = @$"singleSelectOptionId: ""{optionId}""";
        await UpdateIssueField(projectId, issueNodeId, fieldId, value);
    }

    private async Task UpdateIssueField(string projectId, string issueNodeId, string fieldId, string optionIdOrValue)
    {
        string query = @$"mutation {{
                updateProjectV2ItemFieldValue(input: {{
                    projectId: ""{projectId}"" 
                    itemId: ""{issueNodeId}""
                    fieldId: ""{fieldId}""
                    value: {{
                        {optionIdOrValue}
                    }}
                }}) {{
                    projectV2Item {{
                        id
                    }}
                }}
            }}";

        GitHubGraphQLResponse responseObj = await SendGitHubGraphQLRequestAsync(query);

        try
        {
            if (responseObj.Data.UpdateProjectNextV2Field.ProjectV2Item == null)
            {
                throw new Exception($"Failed to update issue {issueNodeId}'s field {fieldId} to {optionIdOrValue}");
            }
        }
        catch (NullReferenceException e)
        {
            string firstNullField = "(none)";

            if (responseObj is null)
                firstNullField = nameof(responseObj);
            else if (responseObj.Data is null)
                firstNullField = nameof(responseObj.Data);
            else if (responseObj.Data.UpdateProjectNextV2Field is null)
                firstNullField = nameof(responseObj.Data.UpdateProjectNextV2Field);
            else if (responseObj.Data.UpdateProjectNextV2Field.ProjectV2Item is null)
                firstNullField = nameof(responseObj.Data.UpdateProjectNextV2Field.ProjectV2Item);

            _logger.LogError(e, "Unexpected null field \"{NullFieldName}\"", firstNullField);
            throw;
        }
    }

    private async Task<GitHubGraphQLResponse> SendGitHubGraphQLRequestAsync(string query)
    {
        var queryObject = new
        {
            query = query,
            variables = new { }
        };

        using var request = new HttpRequestMessage
        {
            Method = HttpMethod.Post,
            Content = new StringContent(JsonConvert.SerializeObject(queryObject), Encoding.UTF8, "application/json")
        };

        // Add feature header for tracking information
        request.Headers.Add("GraphQL-Features", "tracked_issues_graphql_access");
        using HttpClient httpClient = _clientFactory.GetClient();

        using (var response = await httpClient.SendAsync(request))
        {
            response.EnsureSuccessStatusCode();

            if (response.Content == null)
            {
                throw new HttpRequestException("Response body contains no data");
            }

            string responseString = await response.Content.ReadAsStringAsync();
            
            GitHubGraphQLResponse responseObj = JsonConvert.DeserializeObject<GitHubGraphQLResponse>(responseString);
            HandleErrors(responseObj);

            return responseObj;
        }
    }

    private void HandleErrors(GitHubGraphQLResponse response)
    {
        if (response.Errors != null && response.Errors.Any())
        {
            throw new GitHubGraphQLException(response.Errors);
        }
    }

    private string GetProjectIssuesQuery(string organization, int projectNumber, string endCursor = null)
    {
        string after = string.IsNullOrEmpty(endCursor) ? string.Empty : $", after:\"{endCursor}\"";

        return @$"query{{
                organization(login: ""{organization}"") {{
                    projectV2(number: {projectNumber}) {{
                        items(first: 100 {after}) {{
                            pageInfo {{
                                endCursor
                                hasNextPage
                            }}
                            nodes {{
                                id
                                isArchived
                                fieldValues(first: 20) {{
                                    nodes {{
                                        ... on ProjectV2ItemFieldTextValue {{
                                            __typename
                                            text
                                            field {{
                                                ... on ProjectV2Field {{
                                                    name
                                                }}
                                            }}
                                        }}
                                        ... on ProjectV2ItemFieldSingleSelectValue {{
                                            __typename
                                            name
                                            field {{
                                                ... on ProjectV2Field {{
                                                    name
                                                }}
                                            }}
                                        }}
                                    }}
                                }}
                                content {{
                                    ... on Issue {{
                                        number
                                        title
                                        createdAt
                                        body
                                        url
                                        closed
                                        author {{
                                            login
                                        }}
                                        repository {{
                                            name
                                            nameWithOwner
                                        }}
                                        labels (first: 10) {{
                                            nodes {{
                                                name
                                            }}
                                        }}
                                    }}
                                }}
                            }}
                        }}
                    }}
                }}
            }}";
    }
}
