// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Concurrent;

#nullable enable
namespace Microsoft.DotNet.DarcLib;

public interface ICommentCollector
{
    void AddComment(string comment, CommentType commentType);

    (string Text, CommentType commentType)[] GetComments();

    void ClearComments();
}

public enum CommentType
{
    Information,
    Warning,
    Caution,
}

public class CommentCollector : ICommentCollector
{
    private readonly ConcurrentBag<(string, CommentType)> _comments = [];

    public void AddComment(string comment, CommentType commentType)
    {
        if (!string.IsNullOrWhiteSpace(comment))
        {
            _comments.Add((comment, commentType));
        }
    }

    public void ClearComments()
    {
        _comments.Clear();
    }

    public (string Text, CommentType commentType)[] GetComments()
    {
        return [.. _comments];
    }
}
