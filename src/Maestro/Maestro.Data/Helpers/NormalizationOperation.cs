// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

namespace Maestro.Data.Helpers
{
    public class NormalizationOperation
    {
        private const string VisualStudioDncEngHost = "dnceng.visualstudio.com";
        private const string AzureDevOpsDncEngHost = "dev.azure.com/dnceng";

        /// <summary>
        // If repoUri includes the user in the account we remove it from URIs like
        // https://dnceng@dev.azure.com/dnceng/internal/_git/repo
        // If the URL host is of the form "dnceng.visualstudio.com" like
        // https://dnceng.visualstudio.com/internal/_git/repo we replace it to "dev.azure.com/dnceng"
        // for consistency
        /// </summary>
        /// <param name="url">The original url</param>
        /// <returns>Transformed url</returns>
        public static string NormalizeUrl(string url)
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out Uri parsedUri))
            {
                if (!string.IsNullOrEmpty(parsedUri.UserInfo))
                {
                    url = url.Replace($"{parsedUri.UserInfo}@", string.Empty);
                }

                if (parsedUri.Host == VisualStudioDncEngHost)
                {
                    url = url.Replace(VisualStudioDncEngHost, AzureDevOpsDncEngHost);
                }
            }

            return url;
        }
    }
}
