// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;

#nullable enable
namespace Microsoft.DotNet.Darc.Models;

public abstract class EditorPopUp
{
    public EditorPopUp(string path, List<Line> contents)
    {
        Path = path;
        Contents = contents;
    }

    public EditorPopUp(string path)
    {
        Path = path;
        Contents = [];
    }

    [JsonIgnore]
    public string Path { get; set; }

    [JsonIgnore]
    public List<Line> Contents { get; set; }

    public static IList<Line> OnClose(string path)
    {
        string[] updatedFileContents = File.ReadAllLines(path);
        return GetContentValues(updatedFileContents);
    }

    public abstract Task<int> ProcessContents(IList<Line> contents);

    private static List<Line> GetContentValues(IEnumerable<string> contents)
    {
        List<Line> values = [];

        foreach (string content in contents)
        {
            if (!content.TrimStart().StartsWith("#") && !string.IsNullOrEmpty(content))
            {
                values.Add(new Line(content));
            }
        }

        return values;
    }

    /// <summary>
    /// Retrieve the string that should be displayed to the user.
    /// </summary>
    /// <param name="currentValue">Current value of the setting</param>
    /// <param name="defaultValue">Default value if the current setting value is empty</param>
    /// <param name="isSecret">If secret and current value is not empty, should display ***</param>
    /// <returns>String to display</returns>
    protected static string GetCurrentSettingForDisplay(string? currentValue, string defaultValue, bool isSecret)
    {
        if (!string.IsNullOrEmpty(currentValue))
        {
            return isSecret ? "***" : currentValue;
        }
        else
        {
            return defaultValue;
        }
    }

    /// <summary>
    /// Parses a simple string setting and returns the value to save.
    /// </summary>
    /// <param name="inputSetting">Input string from the file</param>
    /// <returns>
    ///     - Original setting if the setting is secret and value is still all ***
    ///     - Empty string if the setting starts+ends with <>
    ///     - New value if anything else.
    /// </returns>
    protected static string? ParseSetting(string? inputSetting, string originalSetting, bool isSecret)
    {
        //if the setting is null, trimming will throw an exception
        if (string.IsNullOrEmpty(inputSetting))
        {
            return inputSetting;
        }

        string trimmedSetting = inputSetting.Trim();
        if (trimmedSetting.StartsWith('<') && trimmedSetting.EndsWith('>'))
        {
            return string.Empty;
        }
        // If the setting is secret and only contains *, then keep it the same as the input setting
        if (isSecret && trimmedSetting.Length > 0 && trimmedSetting.Replace("*", "") == string.Empty)
        {
            return originalSetting;
        }
        return trimmedSetting;
    }
}

public class Line
{
    public static readonly Line Empty = new(string.Empty, true);

    public Line(string text, bool isComment = false)
    {
        Text = !isComment ? text : $"# {text}";
    }

    public Line()
    {
        Text = null;
    }

    public string? Text { get; set; }
}
