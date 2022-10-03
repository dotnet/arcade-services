using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;

namespace Microsoft.DncEng.Configuration.Extensions;

public class MappedJsonConfigurationSource : JsonConfigurationSource
{
    private readonly TimeSpan _reloadTime;
    private readonly Func<string, string> _mapFunc;
    private readonly IServiceProvider _serviceProvider;

    public MappedJsonConfigurationSource(
        TimeSpan reloadTime,
        Func<string, string> mapFunc,
        IServiceProvider serviceProvider)
    {
        _reloadTime = reloadTime;
        _mapFunc = mapFunc;
        _serviceProvider = serviceProvider;
    }

    public override IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        EnsureDefaults(builder);
        return new MappedJsonConfigurationProvider(this, _reloadTime, _mapFunc, _serviceProvider);
    }
}
