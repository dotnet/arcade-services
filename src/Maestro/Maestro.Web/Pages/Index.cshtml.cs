// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.ApplicationInsights.AspNetCore.Extensions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;

namespace Maestro.Web.Pages
{
    public class IndexModel : PageModel
    {
        public IWebHostEnvironment Environment { get; }

        public IndexModel(IWebHostEnvironment environment, IOptions<ApplicationInsightsServiceOptions> applicationInsightsOptions)
        {
            Environment = environment;
            InstrumentationKey = applicationInsightsOptions.Value.InstrumentationKey;
        }

        public IReadOnlyList<(string name, string file)> Themes { get; private set; }
        public string CurrentThemeFile { get; private set; }
        public string InstrumentationKey { get; }

        public PageResult OnGet()
        {
            Themes = GetThemes();
            CurrentThemeFile = GetCurrentThemeFile();
            return Page();
        }

        public IReadOnlyList<(string name, string file)> GetThemes()
        {
            var assetsJson = Path.Join(Environment.WebRootPath, "assets.json");
            var assets = JObject.Parse(System.IO.File.ReadAllText(assetsJson));

            return assets["styles"].ToObject<JArray>().Select(s => (s["name"].ToString(), s["file"].ToString())).ToList();
        }

        public string GetCurrentThemeFile()
        {
            var selectedThemeName = HttpContext.Request.Cookies["Maestro.Theme"];
            var selectedTheme = Themes.FirstOrDefault(t => t.name == selectedThemeName);
            if (selectedTheme.file == default)
            {
                selectedTheme = Themes.FirstOrDefault(t => t.name == "light");
            }

            return selectedTheme.file;
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
