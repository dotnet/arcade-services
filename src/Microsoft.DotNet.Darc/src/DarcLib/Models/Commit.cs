// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Microsoft.DotNet.DarcLib.Models
{
    public class Commit
    {
        public static Commit Create(LibGit2Sharp.Commit commit) =>
            new Commit(
                commit.Sha,
                commit.MessageShort,
                commit.Message,
                CommitSignature.Create(commit.Author),
                CommitSignature.Create(commit.Committer));

        public Commit(
            string sha,
            string shortMessage,
            string message,
            CommitSignature author,
            CommitSignature committer)
        {
            Sha = sha;
            ShortMessage = shortMessage;
            Message = message;
            Author = author;
            Committer = committer;
        }

        public string Sha { get; }

        public string ShortMessage { get; }
        public string Message { get; }

        public CommitSignature Author { get; }
        public CommitSignature Committer { get; }
    }
}
