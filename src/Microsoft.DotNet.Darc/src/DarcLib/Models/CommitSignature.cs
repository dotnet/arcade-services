// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

namespace Microsoft.DotNet.DarcLib.Models
{
    public class CommitSignature
    {
        public static CommitSignature Create(LibGit2Sharp.Signature signature) =>
            new CommitSignature(
                signature.Name,
                signature.Email,
                signature.When);

        public CommitSignature(
            string name,
            string email,
            DateTimeOffset when)
        {
            Name = name;
            Email = email;
            When = when;
        }

        public string Name { get; }
        public string Email { get; }
        public DateTimeOffset When { get; }
    }
}
