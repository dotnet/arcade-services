// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Fabric;

namespace Microsoft.DotNet.Internal.Health
{
    public class InstanceAccessor : IInstanceAccessor
    {
        private readonly IInstanceAccessor _impl;
        
        public InstanceAccessor(ServiceContext context)
        {
            _impl = new ServiceFabricInstanceAccessor(context);
        }

        public InstanceAccessor()
        {
            _impl = new MachineNameInstanceAccessor();
        }

        public string GetCurrentInstanceName()
        {
            return _impl.GetCurrentInstanceName();
        }
    }
}
