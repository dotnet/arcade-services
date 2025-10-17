// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text;
using Microsoft.DotNet.DarcLib;
using Microsoft.Extensions.Logging;

namespace ProductConstructionService.DependencyFlow;

/// <summary>
/// Service for posting comments to pull requests.
/// This class repurposes the existing PullRequestConflictNotifier functionality
/// and adds the ability to post all collected comments from the scoped IPullRequestCommentService.
/// </summary>
internal interface IPullRequestCommenter
{
    /// <summary>
    /// Posts all collected comments from the scoped IPullRequestCommentService to the specified pull request.
    /// </summary>
    /// <param name="prUrl">The URL of the pull request to comment on</param>
    /// <param name="targetRepository">The target repository URL</param>
    Task PostCollectedCommentsAsync(
        string prUrl,
        string targetRepository,
        IEnumerable<(string oldValue, string newValue)> replacements);
}

internal class PullRequestCommenter : IPullRequestCommenter
{
    private readonly IRemoteFactory _remoteFactory;
    private readonly ICommentCollector _commentService;
    private readonly ILogger<PullRequestCommenter> _logger;

    private const string HelpLine = $"> 💡 You may consult the [FAQ]({PullRequestBuilder.CodeFlowPrFaqUri}) for more information or tag **\\@dotnet/product-construction** for assistance.";

    public PullRequestCommenter(
        IRemoteFactory remoteFactory,
        ICommentCollector commentService,
        ILogger<PullRequestCommenter> logger)
    {
        _remoteFactory = remoteFactory;
        _commentService = commentService;
        _logger = logger;
    }

    public async Task PostCollectedCommentsAsync(
        string prUrl,
        string targetRepository,
        IEnumerable<(string oldValue, string newValue)> replacements)
    {
        var comments = _commentService.GetComments();
        if (comments.Count == 0)
        {
            _logger.LogDebug("No collected comments to post to PR {prUrl}", prUrl);
            return;
        }

        var remote = await _remoteFactory.CreateRemoteAsync(targetRepository);

        foreach (var comment in comments)
        {
            var header = comment.commentType switch
            {
                CommentType.Warning => "> [!IMPORTANT]",
                CommentType.Information => "> [!NOTE]",
                _ => throw new ArgumentOutOfRangeException($"Comment type {comment.commentType} is not supported")
            };

            var commentText = comment.Text;
            
            foreach (var (oldValue, newValue) in replacements)
            {
                commentText = commentText.Replace(oldValue, newValue);
            }
            
            StringBuilder sb = new();
            sb.AppendLine(header);
            foreach (var textLine in commentText.Split(Environment.NewLine))
            {
                sb.AppendLine($"> {textLine}");
            }
            sb.AppendLine(">");
            sb.AppendLine(HelpLine);

            try
            {
                await remote.CommentPullRequestAsync(prUrl, sb.ToString());
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to post collected comment to {prUrl}: {message}", prUrl, e.Message);
            }
        }
    }
}
