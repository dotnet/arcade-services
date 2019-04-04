using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Maestro.Web.Pages
{
    public class ErrorModel : PageModel
    {
        public int StatusCode { get; set; }

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
}
