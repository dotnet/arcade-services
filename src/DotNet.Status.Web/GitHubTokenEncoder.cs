// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using Microsoft.Extensions.Logging;

namespace DotNet.Status.Web
{
    internal static class GitHubTokenEncoder
    {
        internal static byte[] EncodeToken(GitHubTokenData token)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(token.UserId);
                writer.Write(token.TokenId);
                writer.Write(token.Expiration.Ticks);
                writer.Write(token.AccessToken);
                writer.Flush();
                return stream.ToArray();
            }
        }

        internal static bool TryDecodeToken(byte[] encoded, ILogger logger, out GitHubTokenData token)
        {
            try
            {
                using (MemoryStream stream = new MemoryStream(encoded))
                using (BinaryReader reader = new BinaryReader(stream))
                {
                    long userId = reader.ReadInt64();
                    long tokenId = reader.ReadInt64();
                    var expiration = new DateTimeOffset(reader.ReadInt64(), TimeSpan.Zero);
                    string accessToken = reader.ReadString();
                    token = new GitHubTokenData(userId, tokenId, expiration, accessToken);
                }

                return true;
            }
            catch (Exception e)
            {
                logger.LogWarning("Invalid token: {exception}", e);
                token = default;
                return false;
            }
        }
    }
}
