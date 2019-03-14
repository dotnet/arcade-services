// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using System.Xml.XPath;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Maestro.Web.Pages
{
    public class IndexModel : PageModel
    {
        public IHostingEnvironment Environment { get; }

        public IndexModel(IHostingEnvironment environment)
        {
            Environment = environment;
        }

        public PageResult OnGet()
        {
            return Page();
        }

        public HtmlString GetStyleBundles()
        {
            var angularGeneratedIndex = Path.Join(Environment.ContentRootPath, "angular-bundle.html");
            var fileText = System.IO.File.ReadAllText(angularGeneratedIndex);
            var firstLink = fileText.IndexOf("<link", StringComparison.Ordinal);
            var endOfHead = fileText.IndexOf("</head>", StringComparison.Ordinal);
            var links = fileText.Substring(firstLink, endOfHead - firstLink);
            return new HtmlString(links);
        }

        public HtmlString GetScriptBundles()
        {
            var angularGeneratedIndex = Path.Join(Environment.ContentRootPath, "angular-bundle.html");
            var fileText = System.IO.File.ReadAllText(angularGeneratedIndex);
            var firstScript = fileText.IndexOf("<script", StringComparison.Ordinal);
            var endOfBody = fileText.IndexOf("</body>", StringComparison.Ordinal);
            var scripts = fileText.Substring(firstScript, endOfBody - firstScript);
            return new HtmlString(scripts);
        }
    }
}
