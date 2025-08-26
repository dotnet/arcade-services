// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Linq;
using Microsoft.DotNet.ProductConstructionService.Client.Models;

namespace Microsoft.DotNet.Darc.Helpers;

/// <summary>
/// Utility class for categorizing channels into groups for display purposes.
/// Based on the categorization logic used in BarViz.
/// </summary>
public static class ChannelCategorizer
{
    public static List<ChannelCategory> CategorizeChannels(IEnumerable<Channel> channels)
    {
        var channelList = channels.ToList();
        var otherCategory = new ChannelCategory("Other");
        var testCategory = new ChannelCategory("Test");

        var categories = new List<ChannelCategory>
        {
            new ChannelCategory(".NET 11"),
            new ChannelCategory(".NET 10"),
            new ChannelCategory(".NET 9"),
            new ChannelCategory(".NET 8"),
            new ChannelCategory(".NET 6"),
            new ChannelCategory(".NET"),
            new ChannelCategory("VS"),
            new ChannelCategory("Windows"),
            otherCategory,
            testCategory,
        };

        foreach (var channel in channelList)
        {
            bool categorized = false;
            if (string.Equals(channel.Classification, "test", System.StringComparison.OrdinalIgnoreCase))
            {
                testCategory.Channels.Add(channel);
                continue;
            }

            foreach (var category in categories)
            {
                if (channel.Name.StartsWith(category.Name, System.StringComparison.OrdinalIgnoreCase))
                {
                    category.Channels.Add(channel);
                    categorized = true;
                    break;
                }
            }

            if (!categorized)
            {
                otherCategory.Channels.Add(channel);
            }
        }

        // Remove empty categories
        categories = categories
            .Where(c => c.Channels.Any())
            .ToList();

        // Apply specific ordering for certain categories
        categories
            .FirstOrDefault(c => c.Name == ".NET")?
            .Channels.Reverse();

        categories
            .FirstOrDefault(c => c.Name == "VS")?
            .Channels.Reverse();

        return categories;
    }

    public class ChannelCategory
    {
        public string Name { get; }
        public List<Channel> Channels { get; } = [];

        public ChannelCategory(string name)
        {
            Name = name;
        }
    }
}