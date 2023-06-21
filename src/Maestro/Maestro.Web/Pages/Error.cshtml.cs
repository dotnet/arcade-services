// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Maestro.Web.Pages;

public class ErrorModel : PageModel
{
    public new int StatusCode { get; set; }

    public string ErrorMessage { get; set; }

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
