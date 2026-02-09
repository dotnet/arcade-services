// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using BuildInsights.BuildAnalysis.Models;
using System.Threading.Tasks;
using System.Threading;

namespace BuildInsights.BuildAnalysis.Services;

public interface IKnownIssueValidationService
{
    Task ValidateKnownIssue(KnownIssueValidationMessage knownIssueValidationMessage, CancellationToken cancellationToken);
}
