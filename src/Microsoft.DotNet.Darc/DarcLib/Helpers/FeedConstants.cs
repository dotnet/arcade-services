// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.RegularExpressions;

namespace Microsoft.DotNet.DarcLib.Helpers;

/// <summary>
/// Constants related to package feed management
/// </summary>
public class FeedConstants
{
    public static readonly string MaestroManagedPublicFeedPrefix = "darc-pub";
    public static readonly string MaestroManagedInternalFeedPrefix = "darc-int";

    public static readonly Regex MaestroManagedFeedNamePattern = new(@"darc-(?<type>(int|pub))-(?<repository>.+?)-(?<sha>[A-Fa-f0-9]{7,40})-?(?<subversion>\d*)", RegexOptions.Compiled);
    public static readonly Regex MaestroManagedSymbolFeedNamePattern = new(@"darc-(?<type>(int|pub))-sym-(?<repository>.+?)-(?<sha>[A-Fa-f0-9]{7,40})-?(?<subversion>\d*)", RegexOptions.Compiled);

    public static readonly Regex[] MaestroManagedFeedPatterns =
    [
        // Matches package feeds like
        // https://dnceng.pkgs.visualstudio.com/public/_packaging/darc-pub-arcade-fd8184c3fcde81eb27ca4c061c6e171f418d753f-1/nuget/v3/index.json
        new(@"https://(?<organization>\w+).pkgs.visualstudio.com/((public|internal)/){0,1}_packaging/" + MaestroManagedFeedNamePattern + @"/nuget/v\d+/index.json", RegexOptions.Compiled),
        // Matches package feeds like
        // https://pkgs.dev.azure.com/dnceng/public/_packaging/darc-pub-dotnet-wpf-8182abc8/nuget/v3/index.json
        new(@"https://pkgs.dev.azure.com/(?<organization>\w+)/((public|internal)/){0,1}_packaging/" + MaestroManagedFeedNamePattern + @"/nuget/v\d+/index.json", RegexOptions.Compiled)
    ];

    // Matches package feeds like
    // https://dotnet-feed-internal.azurewebsites.net/container/dotnet-core-internal/sig/dsdfasdfasdf234234s/se/2020-02-02/darc-int-dotnet-arcade-services-babababababe-08/index.json
    public static readonly Regex AzureStorageProxyFeedPattern = new(
        @"https://([a-z-]+).azurewebsites.net/container/([^/]+)/sig/\w+/se/([0-9]{4}-[0-9]{2}-[0-9]{2})/" + MaestroManagedFeedNamePattern + "/index.json",
        RegexOptions.Compiled);

    public static readonly string NuGetOrgPackageBaseUrl = "https://api.nuget.org/v3-flatcontainer/";
    public static readonly string NuGetOrgLocation = "https://api.nuget.org/v3/index.json";
}
