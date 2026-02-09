// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

// ReSharper disable once CheckNamespace
// Add to extension namespace for easy addition
namespace BuildInsights.UserSentiment;

public static class SentimentUrlExtensions
{
    public static IServiceCollection AddUserSentiment(
        this IServiceCollection collection,
        Action<SentimentUrlOptions> configure)
    {
        collection.TryAddSingleton<SentimentUrlFactory>();
        collection.TryAddSingleton<SentimentInjectorFactory>();
        collection.AddOptions();
        collection.Configure(configure);

        return collection;
    }
}
