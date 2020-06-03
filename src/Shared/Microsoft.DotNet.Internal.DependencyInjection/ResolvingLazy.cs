using System;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.DotNet.Internal.DependencyInjection
{
    internal class ResolvingLazy<T> : Lazy<T>
    {
        public ResolvingLazy(IServiceProvider services) : base(services.GetRequiredService<T>)
        {
        }
    }
}
