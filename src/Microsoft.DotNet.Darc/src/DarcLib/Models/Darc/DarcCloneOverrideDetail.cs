using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;

namespace Microsoft.DotNet.DarcLib.Models.Darc
{
    public class DarcCloneOverrideDetail
    {
        public static IEnumerable<DarcCloneOverrideDetail> ParseAll(XmlNode xml)
        {
            return
                (xml ?? throw new ArgumentNullException(nameof(xml)))
                .SelectNodes("DarcCloneOverride")
                ?.OfType<XmlNode>()
                .Select(n => new DarcCloneOverrideDetail
                {
                    Repo = n.Attributes[nameof(Repo)].Value?.Trim(),
                    FindDependencies = DarcCloneOverrideFindDependency.ParseAll(n)
                })
                ?? Enumerable.Empty<DarcCloneOverrideDetail>();
        }

        public string Repo { get; set; }

        public IEnumerable<DarcCloneOverrideFindDependency> FindDependencies { get; set; }

    }

    public class DarcCloneOverrideFindDependency
    {
        public static IEnumerable<DarcCloneOverrideFindDependency> ParseAll(XmlNode xml)
        {
            return 
                (xml ?? throw new ArgumentNullException(nameof(xml)))
                .SelectNodes("FindDependency")
                ?.OfType<XmlNode>()
                .Select(n =>
                {
                    var r = new DarcCloneOverrideFindDependency
                    {
                        Name = n.Attributes[nameof(Name)].Value?.Trim(),
                    };

                    if (n.Attributes[nameof(ProductCritical)] is XmlAttribute attr &&
                        bool.TryParse(attr.Value?.Trim(), out bool productCritical))
                    {
                        r.ProductCritical = productCritical;
                    }

                    return r;
                })
                ?? Enumerable.Empty<DarcCloneOverrideFindDependency>();
        }

        public string Name { get; set; }

        public bool? ProductCritical { get; set; }
    }
}
