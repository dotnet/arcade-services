// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Maestro.Web.Api.v2020_02_20.Models
{
    public class AssetAndLocation
    {
        public int AssetId { get; set; }

        public string Location { get; set; }

        public v2018_07_16.Models.LocationType LocationType { get; set; }
    }
}
