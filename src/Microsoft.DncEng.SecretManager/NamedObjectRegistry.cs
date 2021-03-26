using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.DncEng.SecretManager
{
    public class NamedObjectRegistry<T>
    {
        private readonly IServiceProvider _provider;
        private readonly IImmutableDictionary<string, Type> _types;

        public NamedObjectRegistry(IServiceProvider provider)
        {
            _provider = provider;
            var typeMap = ImmutableDictionary.CreateBuilder<string, Type>();
            var types = Assembly.GetExecutingAssembly().DefinedTypes.Where(t => typeof(T).IsAssignableFrom(t)).Where(t => t != typeof(T));
            foreach (var type in types)
            {
                var name = type.GetCustomAttribute<NameAttribute>()?.Name;
                if (string.IsNullOrEmpty(name))
                {
                    throw new InvalidOperationException($"Type {type.Name} is missing a Name attribute.");
                }

                if (typeMap.ContainsKey(name))
                {
                    throw new InvalidOperationException($"Duplicate name for {type.Name} '{name}'");
                }

                typeMap.Add(name, type);
            }

            _types = typeMap.ToImmutable();
        }

        public virtual T Create(string name, IReadOnlyDictionary<string, string> parameters)
        {
            if (!_types.TryGetValue(name, out Type type))
            {
                throw new InvalidOperationException($"{typeof(T).Name} with name '{name}' not found.");
            }

            return (T)ActivatorUtilities.CreateInstance(_provider, type, parameters);
        }
    }
}
