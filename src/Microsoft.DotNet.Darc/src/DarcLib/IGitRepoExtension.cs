// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Microsoft.DotNet.DarcLib
{
    public static class IGitRepoExtension
    {
        public static string GetDecodedContent(this IGitRepo gitRepo, string encodedContent)
        {
            try
            {
                byte[] content = Convert.FromBase64String(encodedContent);
                // We can't use Encoding.UTF8.GetString here because that returns a string containing a BOM if one exists in the bytes.
                using (var str = new MemoryStream(content, false))
                using (var reader = new StreamReader(str))
                {
                    return reader.ReadToEnd();
                }
            }
            catch (FormatException)
            {
                return encodedContent;
            }
        }

        public static byte[] GetContentBytes(this IGitRepo gitRepo, string content)
        {
            string decodedContent = GetDecodedContent(gitRepo, content);
            return Encoding.UTF8.GetBytes(decodedContent);
        }

        const string refsHeadsPrefix = "refs/heads/";
        public static string NormalizeBranchName(string branch)
        {
            if (branch.StartsWith(refsHeadsPrefix))
            {
                return branch.Substring(refsHeadsPrefix.Length);
            }
            return branch;
        }
}
}
