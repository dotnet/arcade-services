// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using System.Xml.XPath;
using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Newtonsoft.Json.Linq;

namespace Maestro.Web.Pages
{
    public class IndexModel : PageModel
    {
        public IHostingEnvironment Environment { get; }
        public TelemetryClient TelemetryClient { get; }

        public IndexModel(IHostingEnvironment environment, TelemetryClient telemetryClient)
        {
            Environment = environment;
            TelemetryClient = telemetryClient;
        }

        public IReadOnlyList<(string name, string file)> Themes { get; private set; }
        public string CurrentThemeFile { get; private set; }
        public string InstrumentationKey => TelemetryClient.InstrumentationKey;

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
