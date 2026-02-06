// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ProductConstructionService.Api.Pages;

public class ErrorModel : PageModel
{
    public new int StatusCode { get; set; }

    public string? ErrorMessage { get; set; }

    public string? ReturnUrl => HttpContext.Features.Get<IStatusCodeReExecuteFeature>()?.OriginalPath;

    public void OnGet(int code)
    {
        StatusCode = code;
        ErrorMessage = HttpContext.Items["ErrorMessage"]?.ToString();
        if (string.IsNullOrEmpty(ErrorMessage))
        {
            ErrorMessage = "Unable to process request.";
        }
    }
}
