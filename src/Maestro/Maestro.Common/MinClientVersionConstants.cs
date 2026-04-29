// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Maestro.Common;

public static class MinClientVersionConstants
{
    /// <summary>Request header that carries the calling client's name (e.g. <c>"darc"</c>).</summary>
    public const string ClientNameHeader = "X-Client-Name";

    /// <summary>Request header that carries the calling client's semver version.</summary>
    public const string ClientVersionHeader = "X-Client-Version";

    /// <summary>
    /// Response header used by the server to communicate the minimum required client version
    /// when rejecting a request with HTTP 426 (Upgrade Required).
    /// </summary>
    public const string MinimumClientVersionHeader = "X-Minimum-Client-Version";

    /// <summary>Canonical client name reported by the darc CLI.</summary>
    public const string DarcClientName = "darc";

    /// <summary>
    /// Redis key under which the server stores the minimum required darc client version.
    /// </summary>
    public const string DarcMinVersionRedisKey = "min-client-version-darc";
}
