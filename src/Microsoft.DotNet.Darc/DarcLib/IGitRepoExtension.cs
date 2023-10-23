// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;

namespace Microsoft.DotNet.DarcLib;

public static class IGitRepoExtension
{
    public static string GetDecodedContent(this IRemoteGitRepo gitRepo, string encodedContent)
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
}
