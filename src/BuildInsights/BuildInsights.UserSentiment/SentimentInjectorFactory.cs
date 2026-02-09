// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace BuildInsights.UserSentiment;

public class SentimentInjectorFactory
{
    private readonly SentimentUrlFactory _url;

    public SentimentInjectorFactory(SentimentUrlFactory url)
    {
        _url = url;
    }

    public FeatureSentimentInjector CreateForFeature(SentimentFeature feature)
    {
        return new FeatureSentimentInjector(_url.CreateForFeature(feature));
    }
}
