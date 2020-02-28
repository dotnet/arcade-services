using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;

namespace Microsoft.DncEng.Configuration.Extensions
{
    public class MappedJsonConfigurationSource : JsonConfigurationSource
    {
        private readonly TimeSpan _reloadTime;
        private readonly Func<string, string> _mapFunc;

        public MappedJsonConfigurationSource(TimeSpan reloadTime, Func<string, string> mapFunc)
        {
            _reloadTime = reloadTime;
            _mapFunc = mapFunc;
        }

        public override IConfigurationProvider Build(IConfigurationBuilder builder)
        {
            EnsureDefaults(builder);
            return new MappedJsonConfigurationProvider(this, _reloadTime, _mapFunc);
        }
    }
}