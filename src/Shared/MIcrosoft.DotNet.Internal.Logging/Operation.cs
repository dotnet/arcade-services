// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace Microsoft.DotNet.Internal.Logging
{
    public sealed class Operation : IDisposable
    {
        private List<IDisposable> _toDispose;
        private Activity _activity;

        internal Operation(IServiceProvider serviceProvider)
        {
            ServiceProvider = serviceProvider;
        }

        internal Operation TrackDisposable(IDisposable track)
        {
            if (_toDispose == null)
                _toDispose = new List<IDisposable>();
            _toDispose.Add(track);
            return this;
        }

        public IServiceProvider ServiceProvider { get; }

        public void Dispose()
        {

            List<Exception> ex = null;

            void Report(Exception e)
            {
                if (ex == null)
                    ex = new List<Exception>();
                ex.Add(e);
            }
            
            List<IDisposable> list = Interlocked.Exchange(ref _toDispose, null);
            if (list != null)
            {
                foreach (IDisposable item in Enumerable.Reverse(list))
                {
                    try
                    {
                        item?.Dispose();
                    }
                    catch (Exception e)
                    {
                        Report(e);
                    }
                }
            }

            Activity activity = Interlocked.Exchange(ref _activity, null);
            if (activity != null)
            {
                try
                {
                    activity.Stop();
                }
                catch (Exception e)
                {
                    Report(e);
                }
            }

            if (ex != null)
            {
                throw new AggregateException(ex);
            }
        }

        public void TrackActivity(Activity activity)
        {
            _activity = activity;
        }
    }
}
