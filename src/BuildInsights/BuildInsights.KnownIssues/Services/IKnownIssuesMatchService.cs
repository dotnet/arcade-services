// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using BuildInsights.KnownIssues.Models;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace BuildInsights.KnownIssues.Services;

public interface IKnownIssuesMatchService
{
    Task<List<KnownIssue>> GetKnownIssuesInStream(Stream stream, IReadOnlyList<KnownIssue> knownIssues);
    List<KnownIssue> GetKnownIssuesInString(string errorLine, IReadOnlyList<KnownIssue> knownIssues);
}
