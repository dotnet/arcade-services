using System;
using System.Threading;

namespace Microsoft.DotNet.Services.Utility
{
    public class Scoped<T>
    {
        /// <summary>
        /// 1 if the value is already set. Can't be a bool because Interlocked.Exchange doesn't work on bool
        /// </summary>
        private int _oneIfSet;
        private T _value;

        public bool IsInitialized => _oneIfSet == 1;
        public T Value
        {
            get
            {
                if (_oneIfSet != 1)
                {
                    throw new InvalidOperationException("Value not set in scope");
                }

                return _value;
            }
        }

        public void Initialize(T value)
        {
            if (Interlocked.Exchange(ref _oneIfSet, 1) == 1)
            {
                throw new InvalidOperationException("Value set multiple times in single scope");
            }

            _value = value;
        }
    }
}
