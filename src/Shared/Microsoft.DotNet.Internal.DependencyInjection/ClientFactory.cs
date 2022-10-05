using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace Microsoft.DotNet.Internal.DependencyInjection;

public class ClientFactory<TOptions, TClient> : IClientFactory<TClient>, IDisposable
    where TClient : class
{
    private readonly Dictionary<string, RefCountedObject<TClient>> _clients = new Dictionary<string, RefCountedObject<TClient>>();
    private readonly IOptionsMonitor<TOptions> _optionsMonitor;
    private readonly Func<TOptions, TClient> _factory;
    private readonly IDisposable _optionsChangeRegistration;

    public ClientFactory(IOptionsMonitor<TOptions> optionsMonitor, Func<TOptions, TClient> factory)
    {
        _optionsMonitor = optionsMonitor;
        _factory = factory;

        _optionsChangeRegistration = optionsMonitor.OnChange(OptionsChanged);
    }

    public Reference<TClient> GetClient(string name)
    {
        lock (_clients)
        {
            if (!_clients.TryGetValue(name, out RefCountedObject<TClient> client))
            {
                client = _clients[name] = CreateClient(name);
            }
            return new Reference<TClient>(client);
        }
    }

    private void OptionsChanged(TOptions options, string name)
    {
        RefCountedObject<TClient> oldClientToRelease = null;
        lock (_clients)
        {
            if (_clients.TryGetValue(name, out oldClientToRelease))
            {
                _clients.Remove(name);
            }

            _clients[name] = CreateClient(name);
        }
        oldClientToRelease?.Release();
    }

    private RefCountedObject<TClient> CreateClient(string name)
    {
        var value = new RefCountedObject<TClient>(_factory(_optionsMonitor.Get(name)));
        value.AddRef();
        return value;
    }

    public void Dispose()
    {
        _optionsChangeRegistration?.Dispose();
        lock (_clients)
        {
            foreach (RefCountedObject<TClient> client in _clients.Values)
            {
                client.Release();
            }

            _clients.Clear();
        }
    }
}
