// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;

namespace BuildInsights.UserSentiment;

public readonly struct FeatureSentimentInjector
{
    private readonly FeatureSentimentUrl _url;

    internal FeatureSentimentInjector(FeatureSentimentUrl url)
    {
        _url = url;
    }

    public FeatureSentimentInjector WithProperties(IImmutableDictionary<string, string> properties)
    {
        return new FeatureSentimentInjector(_url.WithProperties(properties));
    }

    public FeatureSentimentInjector WithProperty(string key, string value)
    {
        return new FeatureSentimentInjector(_url.WithProperty(key, value));
    }

    /// <summary>
    ///     Get a segment markdown encoded text that can be embedded to allow for user sentiment feedback for a given feature
    ///     with properties
    /// </summary>
    /// <param name="feature">The feature ID to record sentiment for</param>
    /// <param name="properties">Any properties associated with the feature to record</param>
    /// <param name="staging">True to use the staging helix environment, false to use the production environment</param>
    /// <returns>Markdown encoded text to append for a user sentiment question</returns>
    public string GetMarkdown()
    {
        (string pImg, string pForm, string nImg, string nForm) = _url.GetUrls();
        return $"<sub>Was this helpful? [![Yes]({pImg})]({pForm}) [![No]({nImg})]({nForm})</sub>";
    }

    public string GetHtml(bool popOut)
    {
        (string pImg, string pForm, string nImg, string nForm) = _url.GetUrls();
        string popOutAttr = popOut ? " target=\"_blank\"" : "";
        return $"<span style=\"font-size: smaller\">Was this helpful? <a href=\"{pForm}\"{popOutAttr}><img src=\"{pImg}\" alt=\"Yes\" /></a> <a href=\"{nForm}\"{popOutAttr}><img src=\"{nImg}\" alt=\"No\" /></a></span>";
    }

    public string GetTrackingLink(int linkId, string targetUrl)
    {
        return _url.GetTrackingLink(linkId, targetUrl);
    }
}
