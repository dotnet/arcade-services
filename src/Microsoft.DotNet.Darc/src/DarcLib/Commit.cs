// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Microsoft.DotNet.DarcLib
{
    public class Commit
    {
        public Commit(string author, string sha, string message)
        {
            Author = author;
            Sha = sha;
            Message = message;

        }

        public string Author { get; set; }
        public string Sha { get; set; }
        public string Message { get; set; }
    }
}
