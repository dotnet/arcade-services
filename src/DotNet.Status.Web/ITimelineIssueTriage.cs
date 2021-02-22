// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using DotNet.Status.Web.Controllers;
using System.Threading.Tasks;
using DotNet.Status.Web.Models;

namespace DotNet.Status.Web
{
    public interface ITimelineIssueTriage
    {
        Task ProcessIssueEvent(IssuesHookData issuePayload);
    }
}
