// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.Options;

namespace BuildInsights.UserSentiment;

public class SentimentUrlFactory
{
    private readonly IOptions<SentimentUrlOptions> _options;

    public SentimentUrlFactory(IOptions<SentimentUrlOptions> options)
    {
        _options = options;
    }

    public FeatureSentimentUrl CreateForFeature(SentimentFeature feature)
    {
        return new FeatureSentimentUrl(_options.Value.Host, feature);
    }

    public FeatureSentimentUrl CreateForFeature(int featureId)
    {
        return new FeatureSentimentUrl(_options.Value.Host, featureId);
    }
}
