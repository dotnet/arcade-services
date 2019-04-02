// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Microsoft.DotNet.DarcLib
{
    public static class AzureDevOpsReleaseStatus
    {
        /// <summary>
        /// A successfull release.
        /// </summary>
        public const string Succeeded = "succeeded";
        /// <summary>
        /// A failed release.
        /// </summary>
        public const string Rejected = "rejected";
    }
}
