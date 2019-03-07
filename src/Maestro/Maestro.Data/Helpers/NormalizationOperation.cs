// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

namespace Maestro.Data.Helpers
{
    public class NormalizationOperation
    {
        /// <summary>
        // If repoUri includes the user in the account we remove it from URIs like
        // https://dnceng@dev.azure.com/dnceng/internal/_git/repo
        /// </summary>
        /// <param name="url">The original url</param>
        /// <returns>Transformed url</returns>
        public static string RemoveUserFromUrl(string url)
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out Uri parsedUri))
            {
                if (!string.IsNullOrEmpty(parsedUri.UserInfo))
                {
                    url = url.Replace($"{parsedUri.UserInfo}@", string.Empty);
                }
            }

            return url + "something";
        }
    }
}
