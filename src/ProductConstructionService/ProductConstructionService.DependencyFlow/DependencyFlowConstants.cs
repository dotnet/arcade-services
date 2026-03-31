// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace ProductConstructionService.DependencyFlow;

internal static class DependencyFlowConstants
{
#if DEBUG
    internal static readonly TimeSpan DefaultReminderDelay = TimeSpan.FromMinutes(1);
#else
    internal static readonly TimeSpan DefaultReminderDelay = TimeSpan.FromMinutes(5);
#endif
}
