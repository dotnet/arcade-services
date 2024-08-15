// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.ApplicationInsights.AspNetCore.Extensions;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.DotNet.ServiceFabric.ServiceHost;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;

namespace ProductConstructionService.Api.Pages;

public class IndexModel : PageModel
{
    public IWebHostEnvironment Environment { get; }

    public IndexModel(IWebHostEnvironment environment, IOptions<ApplicationInsightsServiceOptions> applicationInsightsOptions)
    {
        Environment = environment;
        ConnectionString = applicationInsightsOptions.Value.ConnectionString;
        Themes = GetThemes();
    }

    public IReadOnlyList<(string name, string file)> Themes { get; private set; }
    public string? CurrentThemeFile { get; private set; }
    public string? ConnectionString { get; }

    public PageResult OnGet()
    {
        CurrentThemeFile = GetCurrentThemeFile();
        return Page();
    }

    public IReadOnlyList<(string name, string file)> GetThemes()
    {
        return ReadAssetsJson()["styles"]?
            .ToObject<JArray>()?
            .Select(s => (s["name"]!.ToString(), s["file"]!.ToString()))
            .ToList() ?? [];
    }

    public string GetCurrentThemeFile()
    {
        var selectedThemeName = HttpContext.Request.Cookies["Maestro.Theme"];
        var selectedTheme = Themes.FirstOrDefault(t => t.name == selectedThemeName);
        if (selectedTheme.file == default)
        {
            selectedTheme = Themes.FirstOrDefault(t => t.name == "light");
        }

        return selectedTheme.file ?? "light.css";
    }

    public HtmlString GetScriptBundles()
    {
        var scripts = ReadAssetsJson()["scripts"]?
            .ToObject<JArray>()?
            .Select(s => $"<script type=\"text/javascript\" src=\"{s["file"]}\"></script>");

        return new HtmlString(string.Join("", scripts ?? []));
    }

    private JObject ReadAssetsJson()
    {
        var path = Environment.EnvironmentName == "Development" && !ServiceFabricHelpers.RunningInServiceFabric()
            ? Path.Join(PcsStartup.LocalCompiledStaticFilesPath, "assets.json")
            : Path.Join(Environment.WebRootPath, "assets.json");

        return JObject.Parse(System.IO.File.ReadAllText(path));
    }
}
