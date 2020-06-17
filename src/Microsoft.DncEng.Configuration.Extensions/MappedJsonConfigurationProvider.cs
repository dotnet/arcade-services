using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Configuration.Json;

namespace Microsoft.DncEng.Configuration.Extensions
{
    public class MappedJsonConfigurationProvider : JsonConfigurationProvider
#if NETCOREAPP2_1
        , IDisposable
#elif NETSTANDARD
#error This project cannot be built with netstandard, please build it for netcoreapp* or net*
#endif
    {
        private readonly Func<string, string> _mapFunc;
        private readonly Timer _timer;
        private IDictionary<string, string> _rawData = new Dictionary<string, string>();

        public MappedJsonConfigurationProvider(MappedJsonConfigurationSource source, TimeSpan reloadTime, Func<string, string> mapFunc) : base(source)
        {
            _mapFunc = mapFunc;
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
            Data = _rawData.ToDictionary(p => p.Key, p => _mapFunc(p.Value));
        }

        private void Reload(object? state)
        {
            MapData();
            OnReload();
        }

#if NETCOREAPP2_1
        public void Dispose()
        {
            _timer.Dispose();
        }
#else
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _timer.Dispose();
            }
            base.Dispose(disposing);
        }
#endif
    }
}
