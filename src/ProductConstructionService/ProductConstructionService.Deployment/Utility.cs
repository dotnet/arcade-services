// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace ProductConstructionService.Deployment;

internal static class Utility
{
    public static async Task<bool> Sleep(int durationSeconds)
    {
        await Task.Delay(TimeSpan.FromSeconds(durationSeconds));
        return true;
    }

}
