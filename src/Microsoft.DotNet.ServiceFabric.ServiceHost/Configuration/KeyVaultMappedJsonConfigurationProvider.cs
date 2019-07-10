// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Microsoft.Extensions.Configuration.Json;

namespace Microsoft.DotNet.ServiceFabric.ServiceHost
{
    public sealed class KeyVaultMappedJsonConfigurationProvider : JsonConfigurationProvider, IDisposable
    {
        private IDictionary<string, string> _rawData;
        private readonly Timer _timer;

        public KeyVaultMappedJsonConfigurationProvider(
            KeyVaultMappedJsonConfigurationSource source,
            TimeSpan reloadTime) : base(source)
        {
            _timer = new Timer(Reload, null, reloadTime, reloadTime);
        }

        public override void Load(Stream stream)
        {
            base.Load(stream);
            _rawData = Data;
            MapData();
        }

        private void MapData()
        {
            Data = ((KeyVaultMappedJsonConfigurationSource) Source).MapKeyVaultReferences(_rawData);
        }

        private void Reload(object state)
        {
            MapData();
            OnReload();
        }

        public void Dispose()
        {
            _timer.Dispose();
        }
    }
}
