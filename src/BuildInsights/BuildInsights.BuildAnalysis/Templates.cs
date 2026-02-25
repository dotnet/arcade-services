// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using HandlebarsDotNet;

namespace BuildInsights.BuildAnalysis;

public static class Templates
{
    private static string BasePath { get; } = Path.GetDirectoryName(typeof(Templates).Assembly.Location)!;

    private static void LoadPartials(IHandlebars hb)
    {
        string partials = Path.GetFullPath(Path.Combine(BasePath, "Templates", "Partials"));

        foreach (string filePath in Directory.EnumerateFiles(partials, "*.hbs", SearchOption.AllDirectories))
        {
            LoadTemplateFile(hb, filePath);
        }
    }

    public static Dictionary<string, HandlebarsTemplate<object, object>> Compile(IHandlebars hb)
    {
        LoadPartials(hb);
        var topLevelTemplates = new Dictionary<string, HandlebarsTemplate<object, object>>();
        foreach (var file in Directory.GetFiles(Path.Combine(BasePath, "Templates"), "*.hbs", SearchOption.TopDirectoryOnly))
        {
            topLevelTemplates.Add(Path.GetFileNameWithoutExtension(file), hb.Compile(File.ReadAllText(file)));
        }
        return topLevelTemplates;
    }

    private static void LoadTemplateFile(IHandlebars hb, string path)
    {
        string reader = File.ReadAllText(path);
        hb.RegisterTemplate(Path.GetFileNameWithoutExtension(path), reader);
    }
}
