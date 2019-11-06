// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.EntityFrameworkCore.Query;
using Newtonsoft.Json.Linq;

namespace Maestro.Data
{
    public static class JsonExtensions
    {
        public static string JsonValue(string column, [NotParameterized] string path)
        {
            // The Entity Framework in memory provider will call this so it needs to be implemented
            var lax = true;
            if (path.StartsWith("lax "))
            {
                path = path.Substring("lax ".Length);
            }
            else if (path.StartsWith("strict "))
            {
                lax = false;
                path = path.Substring("strict ".Length);
            }

            JToken token = JObject.Parse(column).SelectToken(path, !lax);
            return token.ToObject<string>();
        }
    }
}