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
using Newtonsoft.Json.Linq;

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
            var assetsJson = Path.Join(Environment.WebRootPath, "assets.json");
            var assets = JObject.Parse(System.IO.File.ReadAllText(assetsJson));

            var links = assets["styles"].ToObject<JArray>()
                .Select(s => $"<link rel=\"stylesheet\" href=\"{s["file"]}\">");

            return new HtmlString(string.Join("", links));
        }

        public HtmlString GetScriptBundles()
        {
            var assetsJson = Path.Join(Environment.WebRootPath, "assets.json");
            var assets = JObject.Parse(System.IO.File.ReadAllText(assetsJson));

            var scripts = assets["scripts"].ToObject<JArray>()
                .Select(s => $"<script type=\"text/javascript\" src=\"{s["file"]}\"></script>");

            return new HtmlString(string.Join("", scripts));
        }
    }
}
