// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.ServiceFabric.ServiceHost;

namespace Maestro.Web;

internal static class Program
{
    private static void Main()
    {
        ServiceHostWebSite<Startup>.Run("Maestro.WebType");
    }
}
