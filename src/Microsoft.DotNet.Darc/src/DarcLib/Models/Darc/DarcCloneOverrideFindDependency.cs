// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;

namespace Microsoft.DotNet.DarcLib.Models.Darc
{
    public class DarcCloneOverrideFindDependency
    {
        public static IEnumerable<DarcCloneOverrideFindDependency> ParseAll(XmlNode xml)
        {
            return
                (xml ?? throw new ArgumentNullException(nameof(xml)))
                .SelectNodes("FindDependency")
                ?.OfType<XmlNode>()
                .Select(n => new DarcCloneOverrideFindDependency
                {
                    Name = n.Attributes[nameof(Name)].Value?.Trim(),
                    ProductCritical = ParseOptionalBoolAttribute(n, nameof(ProductCritical)),
                    ExcludeFromSourceBuild = ParseOptionalBoolAttribute(n, nameof(ExcludeFromSourceBuild)),
                })
                ?? Enumerable.Empty<DarcCloneOverrideFindDependency>();
        }

        public string Name { get; set; }

        public bool? ProductCritical { get; set; }

        public bool? ExcludeFromSourceBuild { get; set; }

        private static bool? ParseOptionalBoolAttribute(XmlNode node, string name)
        {
            if (node.Attributes[name] is XmlAttribute attribute &&
                bool.TryParse(attribute.Value?.Trim(), out bool result))
            {
                return result;
            }

            return null;
        }
    }
}
