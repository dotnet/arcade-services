// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace ProductConstructionService.Common;

public interface IRedisCacheFactory
{
    IRedisCache<T> Create<T>(string stateKey) where T : class;
    IRedisCache Create(string stateKey);
}

public class RedisCacheFactory : IRedisCacheFactory
{
    private readonly IConnectionMultiplexer _connection;
    private readonly ILogger<RedisCache> _logger;

    public RedisCacheFactory(IConnectionMultiplexer connection, ILogger<RedisCache> logger)
    {
        _connection = connection;
        _logger = logger;
    }

    public IRedisCache<T> Create<T>(string stateKey) where T : class
    {
        return new RedisCache<T>(Create(stateKey), _logger);
    }

    public IRedisCache Create(string stateKey)
    {
        return new RedisCache(_connection, stateKey);
    }
}
