// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;
using System.Text;

namespace BuildInsights.UserSentiment;

public readonly struct FeatureSentimentUrl
{
    private readonly string _host;
    private readonly int _featureId;
    private readonly IImmutableDictionary<string, string>? _properties;

    internal FeatureSentimentUrl(string host, SentimentFeature feature) : this(host, (int) feature)
    {
    }

    internal FeatureSentimentUrl(string host, int featureId) : this(host, featureId, null)
    {
    }

    private FeatureSentimentUrl(string host, int featureId, IImmutableDictionary<string, string>? properties)
    {
        _host = host.TrimEnd('/');
        _featureId = featureId;
        _properties = properties;
    }

    public FeatureSentimentUrl WithProperties(IImmutableDictionary<string, string> properties)
    {
        return new FeatureSentimentUrl(_host, _featureId, properties);
    }

    public FeatureSentimentUrl WithProperty(string key, string value)
    {
        return new FeatureSentimentUrl(_host, _featureId, (_properties ?? ImmutableDictionary<string, string>.Empty).Add(key, value));
    }

    internal (string positiveImage, string positiveForm, string negativeImage, string negativeForm) GetUrls()
    {
        return (PositiveImageUrl,
            PositiveFormUrl,
            NegativeImageUrl,
            NegativeFormUrl);
    }

    public string GetTrackingLink(int linkId, string targetUrl)
    {
        return AddPropertyParameters($"{_host}/f/t/{_featureId}/{linkId}?u={Uri.EscapeDataString(targetUrl)}");
    }

    public string PositiveImageUrl => AddPropertyParameters($"{_host}/f/ip/{_featureId}");

    public string NegativeImageUrl => $"{_host}/f/in";

    public string PositiveFormUrl => AddPropertyParameters($"{_host}/f/p/{_featureId}");

    public string NegativeFormUrl => AddPropertyParameters($"{_host}/f/n/{_featureId}");

    private string AddPropertyParameters(string baseUrl)
    {
        if (_properties == null || _properties.Count == 0)
        {
            return baseUrl;
        }

        var builder = new StringBuilder(baseUrl);
        if (!baseUrl.Contains('?'))
        {
            builder.Append('?');
        }
        else
        {
            builder.Append('&');
        }

        bool added = false;
        // We do this one at a time because the non-one at a time requires a mutable dictionary
        foreach ((string key, string value) in _properties)
        {
            if (added)
            {
                builder.Append('&');
            }

            added = true;

            builder.Append(Uri.EscapeDataString(key));
            builder.Append('=');
            builder.Append(Uri.EscapeDataString(value));
        }

        return builder.ToString();
    }
}
