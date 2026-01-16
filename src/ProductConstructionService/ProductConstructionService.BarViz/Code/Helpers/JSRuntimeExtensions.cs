// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.JSInterop;

namespace ProductConstructionService.BarViz.Code.Helpers;

internal static class JSRuntimeExtensions
{
    public static async Task OpenNewWindow(this IJSRuntime jsRuntime, string uri)
    {
        await jsRuntime.InvokeVoidAsync("open", uri, "_blank");
    }
}
