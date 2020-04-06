using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Fabric;
using System.Linq;
using System.Reflection;
using Autofac;
using Microsoft.DotNet.ServiceFabric.ServiceHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.DotNet.Internal.DependencyInjection.Testing
{
    public static class DependencyInjectionValidation
    {
        private static readonly ImmutableList<Type> s_exemptTypes = ImmutableList.Create(
            typeof(ServiceContext),
            // IConfigure options is a strange type, and built into the options framework, no need to validation
            typeof(IConfigureOptions<>),
            // ILifetimeScope comes from Autofac, and we are only checking the ASP.NET half of this
            typeof(ILifetimeScope)
        );

        private static readonly ImmutableList<string> s_exemptNamespaces = ImmutableList.Create(
            "Microsoft.ApplicationInsights.AspNetCore"
        );

        public static bool IsDependencyResolutionCoherent(Action<ServiceCollection> register, out string errorMessage)
        {
            errorMessage = null;

            Environment.SetEnvironmentVariable("ENVIRONMENT", "XUNIT");

            var services = new ServiceCollection();
            ServiceHost.ConfigureDefaultServices(services);
            register(services);

            foreach (ServiceDescriptor service in services)
            {
                if (service.ImplementationType == null)
                {
                    continue;
                }

                if (IsExemptType(service.ImplementationType) || IsExemptType(service.ServiceType))
                {
                    continue;
                }

                bool foundConstructable = false;
                ConstructorInfo[] constructors = service.ImplementationType
                    .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                    .OrderBy(c => c.GetParameters().Length)
                    .ToArray();

                if (constructors.Length == 0)
                {
                    // zero constructor things are implicitly constructable
                    continue;
                }

                foreach (ConstructorInfo ctor in constructors)
                {
                    bool ctorWorks = true;

                    foreach (ParameterInfo p in ctor.GetParameters())
                    {
                        ServiceDescriptor parameterService = services.FirstOrDefault(s => IsMatchingServiceRegistration(s.ServiceType, p.ParameterType));
                        if (parameterService != null)
                        {
                            continue;
                        }

                        // Save the first error message, since it's likely to be the most useful
                        if (errorMessage == null)
                        {
                            errorMessage = $"Type {service.ImplementationType.FullName} could not find registered definition for parameter {p.Name} of type {p.ParameterType}";
                        }

                        ctorWorks = false;
                        break;
                    }

                    if (ctorWorks)
                    {
                        foundConstructable = true;
                        break;
                    }
                }


                if (!foundConstructable)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsMatchingServiceRegistration(Type serviceType, Type parameterType)
        {
            // If it's options, lets make sure they are configured
            if (parameterType.IsConstructedGenericType)
            {
                Type parameterRoot = parameterType.GetGenericTypeDefinition();
                if (parameterRoot == typeof(IOptions<>) ||
                    parameterRoot == typeof(IOptionsMonitor<>))
                {
                    if (!serviceType.IsConstructedGenericType) return false;

                    Type optionType = parameterType.GenericTypeArguments[0];
                    Type serviceRoot = serviceType.GetGenericTypeDefinition();
                    return serviceRoot == typeof(IConfigureOptions<>) &&
                        serviceType.GenericTypeArguments[0] == optionType;
                }
            }

            if (IsExemptType(parameterType))
            {
                return true;
            }

            if (serviceType == parameterType) return true;
            if (!parameterType.IsConstructedGenericType) return false;
            Type def = parameterType.GetGenericTypeDefinition();
            if (def == typeof(IEnumerable<>))
            {
                // IEnumerable can be zero, and that's fine
                return true;
            }

            return serviceType == def;
        }

        private static bool IsExemptType(Type type)
        {
            if (type.IsConstructedGenericType)
                return IsExemptType(type.GetGenericTypeDefinition());

            return s_exemptTypes.Contains(type) || s_exemptNamespaces.Any(n => type.FullName.StartsWith(n));
        }
    }
}
