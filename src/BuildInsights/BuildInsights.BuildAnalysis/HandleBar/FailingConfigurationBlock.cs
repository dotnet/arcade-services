// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using HandlebarsDotNet;
using HandlebarsDotNet.PathStructure;
using BuildInsights.BuildAnalysis.Models;

namespace BuildInsights.BuildAnalysis.HandleBar;

public class FailingConfigurationBlock : InlineHelper
{
    public override PathInfo Name => "FailingConfigurationBlock";

    public override void Invoke(in EncodedTextWriter output, in HelperOptions options, in Context context, in Arguments arguments)
    {
        Dictionary<string, object> contextDict = (Dictionary<string, object>)context.Value;
        List<FailingConfiguration>? configs = contextDict["FailingConfigurations"] as List<FailingConfiguration>;
        if (configs != null)
        {
            int configCount = configs.Count;

            if (configCount > 0)
            {
                if (configCount > 3)
                {
                    output.WriteSafeString("<details>\n");
                }
                else
                {
                    output.WriteSafeString("<details open>\n");
                }

                if (configCount > 1)
                {
                    output.WriteSafeString("<summary><h4>Failing Configurations (" + configCount + ")</h4></summary>\n\n");
                }
                else
                {
                    output.WriteSafeString("<summary><h4>Failing Configuration</h4></summary>\n\n");
                }

                output.WriteSafeString("<ul>");

                foreach (var fc in configs)
                {
                    output.WriteSafeString("<li>");
                    output.WriteSafeString($"<a href=\"{fc.Configuration.Url}\">{fc.Configuration.Name}</a>");
                    if (fc.TestLogs != null) output.WriteSafeString($"<a href=\"{fc.TestLogs}\">[Details]</a> ");
                    if (fc.HistoryLink != null) output.WriteSafeString($"<a href=\"{fc.HistoryLink}\">[History]</a> ");
                    if (fc.ArtifactLink != null) output.WriteSafeString($"<a href=\"{fc.ArtifactLink}\">[Artifacts]</a> ");
                    output.WriteSafeString("</li>");
                }

                output.WriteSafeString("</ul>");
                output.WriteSafeString("</details>");
            }
        }
    }
}
