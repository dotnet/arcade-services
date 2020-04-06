// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;

namespace Maestro.Contracts
{
    public interface IPullRequest
    {
        string Url { get; set; }

        List<DependencyUpdateSummary> RequiredUpdates { get; set; }
    }
}
