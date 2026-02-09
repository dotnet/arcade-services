// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace BuildInsights.UserSentiment;

public class SentimentUrlOptions
{
    public string Host { get; set; } = "https://helix.dot.net";

    public static SentimentUrlOptions ForStaging()
    {
        return new SentimentUrlOptions {Host = "https://helix.int-dot.net"};
    }

    public static SentimentUrlOptions ForProduction()
    {
        return new SentimentUrlOptions {Host = "https://helix.dot.net"};
    }
}
