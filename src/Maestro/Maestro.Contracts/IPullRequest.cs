// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;

namespace Maestro.Contracts;

public interface IPullRequest
{
    string Url { get; set; }

    /// <summary>
    /// Indicates whether the last coherency update is successful.
    /// </summary>
    bool CoherencyCheckSuccessful { get; set; }

    /// <summary>
    /// In case of coherency algorithm failure, the coherency error message
    /// provides a list of dependencies that caused the failure.
    /// </summary>
    string CoherencyErrorMessage { get; set; }

    List<DependencyUpdateSummary> RequiredUpdates { get; set; }
}
