// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;

#nullable enable
namespace Microsoft.DotNet.DarcLib;

public interface ICommentCollector
{
    void AddComment(string comment, CommentType commentType);

    IReadOnlyList<(string Text, CommentType commentType)> GetComments();
}

public enum CommentType
{
    Information,
    Warning,
    Caution,
}

public class CommentCollector : ICommentCollector
{
    private readonly List<(string, CommentType)> _comments = [];

    public void AddComment(string comment, CommentType commentType)
    {
        if (!string.IsNullOrWhiteSpace(comment))
        {
            _comments.Add((comment, commentType));
        }
    }

    public IReadOnlyList<(string Text, CommentType commentType)> GetComments()
    {
        return _comments.AsReadOnly();
    }
}
