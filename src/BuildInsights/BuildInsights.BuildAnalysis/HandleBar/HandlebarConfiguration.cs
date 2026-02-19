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
        services.TryAddSingleton<HandlebarHelpers>();
        services.TryAddSingleton<IHelperDescriptor<HelperOptions>, MarkdownLinkHelper>();
        services.TryAddSingleton<IHelperDescriptor<HelperOptions>, HtmlLinkHelper>();
        services.TryAddSingleton<IHelperDescriptor<HelperOptions>, SnapshotIdCommentHelper>();
        services.TryAddSingleton<IHelperDescriptor<HelperOptions>, TruncateHelper>();
        services.TryAddSingleton<IHelperDescriptor<HelperOptions>, RenderKnownLinks>();
        services.TryAddSingleton<IHelperDescriptor<HelperOptions>, SplitMessageIntoCollapsibleSectionsByLength>();
        services.TryAddSingleton<IHelperDescriptor<HelperOptions>, FailingConfigurationBlock>();
        return services;
    }
}
