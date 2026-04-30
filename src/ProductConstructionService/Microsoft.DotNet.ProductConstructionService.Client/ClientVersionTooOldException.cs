// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

#nullable enable
namespace Microsoft.DotNet.ProductConstructionService.Client
{
    /// <summary>
    /// Thrown when the server rejects a request with HTTP 426 (Upgrade Required) because the
    /// reported client version is below the server-configured minimum.
    /// </summary>
    /// <remarks>
    /// Intentionally inherits from <see cref="Exception"/> rather than <see cref="RestApiException"/>
    /// so existing per-operation <c>catch (RestApiException ...)</c> filters do not swallow it.
    /// </remarks>
    [Serializable]
    public class ClientVersionTooOldException : Exception
    {
        /// <summary>
        /// Value sent by the client in the <c>X-Client-Name</c> header (e.g. <c>"darc"</c>).
        /// </summary>
        public string? ClientName { get; }

        /// <summary>
        /// Value sent by the client in the <c>X-Client-Version</c> header.
        /// </summary>
        public string? CurrentVersion { get; }

        /// <summary>
        /// Minimum version reported by the server in the <c>X-Minimum-Client-Version</c>
        /// response header, if present.
        /// </summary>
        public string? MinimumVersion { get; }

        public ClientVersionTooOldException(
            string? clientName,
            string? currentVersion,
            string? minimumVersion,
            RestApiException innerException)
            : base(BuildMessage(clientName, currentVersion, minimumVersion), innerException)
        {
            ClientName = clientName;
            CurrentVersion = currentVersion;
            MinimumVersion = minimumVersion;
        }

        private static string BuildMessage(string? clientName, string? currentVersion, string? minimumVersion) =>
            $"Client '{clientName ?? string.Empty}' version '{currentVersion ?? string.Empty}' is below the minimum required version '{minimumVersion ?? string.Empty}'."
                + " Please run `eng/common/darc-init.ps1` (Windows) or `eng/common/darc-init.sh` (Linux/macOS) to install the latest version.";
    }
}
