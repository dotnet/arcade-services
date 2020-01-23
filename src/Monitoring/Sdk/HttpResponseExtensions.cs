// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Net.Http;
using System.Threading.Tasks;

namespace Microsoft.DotNet.Monitoring.Sdk
{
    internal static class HttpResponseExtensions
    {
        public static async Task EnsureSuccessWithContentAsync(this HttpResponseMessage message)
        {
            if (message.IsSuccessStatusCode)
                return;

            string content = await message.Content.ReadAsStringAsync();
            if (content.Length > 1000)
                content = content.Substring(0, 1000) + "...";

            throw new HttpRequestException($"Response status code does not indicate success: {(int) message.StatusCode} ({message.ReasonPhrase}): Body: {content}");
        }
    }
}
