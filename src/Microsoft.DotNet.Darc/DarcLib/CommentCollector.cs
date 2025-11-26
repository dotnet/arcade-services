// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Concurrent;

#nullable enable
namespace Microsoft.DotNet.DarcLib;

public interface ICommentCollector
{
    void AddComment(string comment, CommentType commentType);

    Comment[] GetComments();
}

public enum CommentType
{
    Information,
    Warning,
    Caution,
}

public class CommentCollector : ICommentCollector
{
    private readonly ConcurrentBag<Comment> _comments = [];

    public void AddComment(string comment, CommentType commentType)
    {
        if (!string.IsNullOrWhiteSpace(comment))
        {
            _comments.Add(new(comment, commentType));
        }
    }

    public Comment[] GetComments()
    {
        return [.. _comments];
    }
}

public record Comment(string Text, CommentType Type);
