// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace DotNet.Status.Web.Options
{
    public class AzureTableTokenStoreOptions
    {
        public string TableUri { get; set; }
        public string TableSasToken { get; set; }
    }
}
