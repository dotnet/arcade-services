// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Maestro.WorkItems;

/// <summary>
/// The only values stored in the desired and observed queue processing state keys.
/// </summary>
public enum WorkItemProcessorState
{
    /// <summary>
    /// The processor takes and processes new work items.
    /// </summary>
    Working,

    /// <summary>
    /// The processor neither accepts nor processes work items.
    /// </summary>
    Stopped,
}
