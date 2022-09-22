using System;
using System.Threading;

namespace Microsoft.DotNet.Internal.DependencyInjection
{
    public sealed class RefCountedObject<T>
    {
        private int _refCount;

        public RefCountedObject(T value)
        {
            _refCount = 0;
            Value = value;
        }

        public T Value { get; }

        public void AddRef()
        {
            Interlocked.Increment(ref _refCount);
        }

        public void Release()
        {
            var newRefCount = Interlocked.Decrement(ref _refCount);
            if (newRefCount == 0)
            {
                if (Value is IDisposable disposable)
                {
                    disposable.Dispose();
                }
                else if (Value is IAsyncDisposable asyncDisposable)
                {
                    asyncDisposable.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
            }
        }
    }

    public struct Reference<T> : IDisposable
    {
        private readonly RefCountedObject<T> _referencedObject;

        public Reference(RefCountedObject<T> value)
        {
            value.AddRef();
            _referencedObject = value;
        }

        public T Value => _referencedObject.Value;

        public void Dispose()
        {
            _referencedObject.Release();
        }
    }
}
