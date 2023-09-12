// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Maestro.ContainerApp.Api.Models;

public class Commit
{
    public Commit(string author, string sha, string message)
    {
        Author = author;
        Sha = sha;
        Message = message ?? string.Empty;
    }

    public string Author { get; }
    public string Sha { get; }
    public string Message { get; }
}
