using System;

namespace Microsoft.DotNet.Internal.Testing.DependencyInjection.Abstractions
{
    [AttributeUsage(AttributeTargets.Class)]
    public class TestDependencyInjectionSetupAttribute : Attribute
    {
    }
}
