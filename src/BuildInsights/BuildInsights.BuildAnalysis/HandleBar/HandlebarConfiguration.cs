// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using HandlebarsDotNet;
using HandlebarsDotNet.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BuildInsights.BuildAnalysis.HandleBar;

public static class HandlebarConfiguration
{
    public static IServiceCollection AddHandleBarHelpers(this IServiceCollection services)
    {
        services.AddSingleton<HandlebarHelpers>();
        services.AddSingleton<IHelperDescriptor<HelperOptions>, MarkdownLinkHelper>();
        services.AddSingleton<IHelperDescriptor<HelperOptions>, HtmlLinkHelper>();
        services.AddSingleton<IHelperDescriptor<HelperOptions>, SnapshotIdCommentHelper>();
        services.AddSingleton<IHelperDescriptor<HelperOptions>, TruncateHelper>();
        services.AddSingleton<IHelperDescriptor<HelperOptions>, RenderKnownLinks>();
        services.AddSingleton<IHelperDescriptor<HelperOptions>, SplitMessageIntoCollapsibleSectionsByLength>();
        services.AddSingleton<IHelperDescriptor<HelperOptions>, FailingConfigurationBlock>();
        return services;
    }
}
