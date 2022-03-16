using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.ApplicationInsights;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.DncEng.Configuration.Extensions
{
    public class MappedJsonConfigurationProvider : JsonConfigurationProvider
    {
        private readonly Func<string, string> _mapFunc;
        private readonly Timer _timer;
        private readonly TelemetryClient _telemetry;
        private IDictionary<string, string> _rawData = new Dictionary<string, string>();

        public MappedJsonConfigurationProvider(
            MappedJsonConfigurationSource source,
            TimeSpan reloadTime,
            Func<string, string> mapFunc,
            IServiceProvider serviceProvider) : base(source)
        {
            _mapFunc = mapFunc;
            _timer = new Timer(Reload, null, reloadTime, reloadTime);
            _telemetry = serviceProvider.GetService<TelemetryClient>();
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
            try
            {
                MapData();
                OnReload();
            }
            catch (Exception e)
            {
                // This exception, because it's in a System.Threading.Timer
                // is going to crash the process, if we are reporting to AppInsights, let's send it to the channel
                // and flush it, so that it gets recorded somewhere
                _telemetry?.TrackException(e);
                _telemetry?.Flush();
                // Then rethrow it... resume destroying the process
                throw;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _timer.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
