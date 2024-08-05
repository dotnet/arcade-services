// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Maestro.Data.Models;

#nullable enable
namespace Maestro.Web.Pages.DependencyFlow;

public record IncomingRepo(
    Build LastConsumedBuild,
    string ShortName,
    Build? OldestPublishedButUnconsumedBuild,
    string CommitUrl,
    string BuildUrl,
    int? CommitDistance,
    DateTimeOffset? CommitAge);
#nullable restore
