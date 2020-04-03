// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Linq;
using System.Xml;

namespace Microsoft.DotNet.DarcLib
{
    public class DependencyDetail
    {
        public static IEnumerable<DependencyDetail> ParseAll(XmlDocument document, bool includePinned = true)
        {
            List<DependencyDetail> dependencyDetails = new List<DependencyDetail>();

            BuildDependencies(document.DocumentElement.SelectNodes("//Dependency"));

            void BuildDependencies(XmlNodeList dependencies)
            {
                if (dependencies.Count > 0)
                {
                    foreach (XmlNode dependency in dependencies)
                    {
                        if (dependency.NodeType != XmlNodeType.Comment && dependency.NodeType != XmlNodeType.Whitespace)
                        {
                            DependencyType type;
                            switch (dependency.ParentNode.Name)
                            {
                                case "ProductDependencies":
                                    type = DependencyType.Product;
                                    break;
                                case "ToolsetDependencies":
                                    type = DependencyType.Toolset;
                                    break;
                                default:
                                    throw new DarcException($"Unknown dependency type '{dependency.ParentNode.Name}'");
                            }

                            bool isPinned = false;

                            // If the 'Pinned' attribute does not exist or if it is set to false we just not update it 
                            if (dependency.Attributes[VersionFiles.PinnedAttributeName] != null)
                            {
                                if (!bool.TryParse(dependency.Attributes[VersionFiles.PinnedAttributeName].Value, out isPinned))
                                {
                                    throw new DarcException($"The '{VersionFiles.PinnedAttributeName}' attribute is set but the value " +
                                        $"'{dependency.Attributes[VersionFiles.PinnedAttributeName].Value}' " +
                                        $"is not a valid boolean...");
                                }
                            }

                            DependencyDetail dependencyDetail = new DependencyDetail
                            {
                                Name = dependency.Attributes[VersionFiles.NameAttributeName].Value?.Trim(),
                                RepoUri = dependency.SelectSingleNode(VersionFiles.UriElementName).InnerText?.Trim(),
                                Commit = dependency.SelectSingleNode(VersionFiles.ShaElementName)?.InnerText?.Trim(),
                                Version = dependency.Attributes[VersionFiles.VersionAttributeName].Value?.Trim(),
                                CoherentParentDependencyName = dependency.Attributes[VersionFiles.CoherentParentAttributeName]?.Value?.Trim(),
                                CoherentProducts = dependency
                                    .SelectNodes(VersionFiles.CoherentProductElementName)
                                    .OfType<XmlNode>()
                                    .Select(x => x.InnerText.Trim())
                                    .ToArray(),
                                Pinned = isPinned,
                                Type = type
                            };

                            dependencyDetails.Add(dependencyDetail);
                        }
                    }
                }
            }

            if (includePinned)
            {
                return dependencyDetails;
            }

            return dependencyDetails.Where(d => !d.Pinned);

        }

        public DependencyDetail()
        {
            CoherentProducts = new List<string>();
            Locations = new List<string>();
        }

        public DependencyDetail(DependencyDetail other)
        {
            Name = other.Name;
            Version = other.Version;
            RepoUri = other.RepoUri;
            Commit = other.Commit;
            Pinned = other.Pinned;
            Type = other.Type;
            CoherentParentDependencyName = other.CoherentParentDependencyName;
            CoherentProducts = other.CoherentProducts;
            Locations = other.Locations;
        }

        public string Name { get; set; }

        /// <summary>
        ///     Version of dependency.
        /// </summary>
        public string Version { get; set; }

        /// <summary>
        ///     Source repository uri that the dependency was produced from.
        /// </summary>
        public string RepoUri { get; set; }

        /// <summary>
        ///     Source commit that the dependency was produced from.
        /// </summary>
        public string Commit { get; set; }

        /// <summary>
        ///     True if the dependency should not be updated, false otherwise.
        /// </summary>
        public bool Pinned { get; set; }

        /// <summary>
        ///     Type of dependency (e.g. Product or Toolset).
        /// </summary>
        public DependencyType Type { get; set; }

        /// <summary>
        ///     Another dependency for which this dependency must be coherent with.
        ///     This means:
        ///     If I have 3 repositories which have a potentially incoherent dependency structure:
        ///     A
        ///     |\
        ///     B |
        ///     \ |
        ///      C
        ///     A different version of C could appear in A and B.
        ///     This may not be a problem, or it could be undesirable.
        ///     This can be resolved to be always coherent by identifying that A's dependency on C
        ///     must be coherent with parent B. Specifically, this means that the build that produced B must
        ///     also have an input build that produced C.
        ///     
        ///     Concretely for .NET Core, core-setup has a dependency on Microsoft.Private.CoreFx.NETCoreApp produced
        ///     in corefx, and Microsoft.NETCore.Runtime.CoreCLR produced in coreclr.  corefx has a dependency on
        ///     Microsoft.NETCore.Runtime.CoreCLR. This means that when updating Microsoft.Private.CoreFx.NETCoreApp
        ///     in core-setup, also update Microsoft.NETCore.Runtime.CoreCLR to the version used to produce that
        ///     Microsoft.Private.CoreFx.NETCoreApp. By corrolary, that means Microsoft.NETCore.Runtime.CoreCLR cannot
        ///     be updated unless that version exists in the subtree of Microsoft.Private.CoreFx.NETCoreApp.
        ///     
        ///     Coherent parent dependencies are specified in Version.Details.xml as follows:
        ///     <![CDATA[
        ///         <Dependency Name="Microsoft.NETCore.App" Version="1.0.0-beta.19151.1" >
        ///             <Uri>https://github.com/dotnet/core-setup</Uri>
        ///             <Sha>abcd</Sha>
        ///         </Dependency>
        ///         <Dependency Name="Microsoft.Private.CoreFx.NETCoreApp" Version="1.2.3" CoherentParentDependency="Microsoft.NETCore.App">
        ///             <Uri>https://github.com/dotnet/corefx</Uri>
        ///             <Sha>defg</Sha>
        ///         </Dependency>
        ///      ]]>
        /// </summary>
        /// 
        public string CoherentParentDependencyName { get; set; }

        /// <summary>
        /// The products that end up taking a dependency on the version of this dependency. As of
        /// writing, the only product is "SDK". In a transitive source-build repository graph
        /// building the SDK, there must be one DependencyDetail per target RepoUri that defines SDK
        /// as a product. This allows "darc clone" to create a synthetically coherent graph by
        /// choosing dependencies that declare SDK in Products, rather than other dependencies that
        /// may be in the graph erroneously.
        ///
        /// The goal is similar to a coherent parent dependency, but the result is not stored in the
        /// repo and artificial coherency does not apply to Microsoft builds.
        /// </summary>
        public IEnumerable<string> CoherentProducts { get; set; }

        /// <summary>
        /// Asset locations for the dependency
        /// </summary>
        public IEnumerable<string> Locations { get; set; }
    }
}
