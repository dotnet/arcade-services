using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.DncEng.SecretManager;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddNamedFromAssembly<T>(this IServiceCollection services, Assembly assembly)
    {
        foreach (TypeInfo type in assembly.DefinedTypes)
        {
            if (!typeof(T).IsAssignableFrom(type))
            {
                continue;
            }

            if (type.IsAbstract)
            {
                continue;
            }

            var nameAttribute = type.GetCustomAttribute<NameAttribute>();
            if (nameAttribute == null)
            {
                continue;
            }

            services.AddSingleton(typeof(T), type);
        }

        return services;
    }
}

public class NamedObjectRegistry<T>
{
    private readonly Dictionary<string, T> _objects;

    protected NamedObjectRegistry()
    {
    }

    public NamedObjectRegistry(IEnumerable<T> objects)
    {
        _objects = objects.ToDictionary(o =>
            o.GetType().GetCustomAttribute<NameAttribute>()?.Name ??
            throw new InvalidOperationException($"Type {o.GetType()} has no NameAttribute"));
    }

    public virtual T Get(string name)
    {
        return _objects[name];
    }
}
