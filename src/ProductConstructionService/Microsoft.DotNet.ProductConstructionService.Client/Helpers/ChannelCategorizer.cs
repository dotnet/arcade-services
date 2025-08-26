// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.DotNet.ProductConstructionService.Client.Models;

namespace Microsoft.DotNet.ProductConstructionService.Client.Helpers
{
    /// <summary>
    /// Utility class for categorizing channels into groups for display purposes.
    /// Based on the categorization logic used in BarViz.
    /// </summary>
    public static class ChannelCategorizer
    {
        private static readonly List<string> s_categoryNames = 
            // .NET 6-20
            Enumerable.Range(0, 16).Select(v => $".NET {20 - v}")
                .Concat(new[]
                {
                    ".NET",
                    "VS", 
                    "Windows",
                    "Other",
                    "Test",
                })
                .ToList();

        public static List<ChannelCategory> CategorizeChannels(IEnumerable<Channel> channels)
        {
            // Create fresh category instances for each call to avoid static state issues
            var categories = s_categoryNames.Select(name => new ChannelCategory(name)).ToList();
            var otherCategory = categories.First(c => c.Name == "Other");
            var testCategory = categories.First(c => c.Name == "Test");

            foreach (var channel in channels)
            {
                bool categorized = false;
                if (string.Equals(channel.Classification, "test", StringComparison.OrdinalIgnoreCase))
                {
                    testCategory.Channels.Add(channel);
                    continue;
                }

                foreach (var category in categories)
                {
                    if (channel.Name.StartsWith(category.Name, StringComparison.OrdinalIgnoreCase))
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
            public List<Channel> Channels { get; } = new List<Channel>();

            public ChannelCategory(string name)
            {
                Name = name;
            }
        }
    }
}
