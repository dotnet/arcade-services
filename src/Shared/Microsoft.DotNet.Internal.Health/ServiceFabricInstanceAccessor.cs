// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Fabric;

namespace Microsoft.DotNet.Internal.Health
{
    public class ServiceFabricInstanceAccessor : IInstanceAccessor
    {
        private readonly ServiceContext _context;
        
        public ServiceFabricInstanceAccessor(ServiceContext context)
        {
            _context = context;
        }

        public string GetCurrentInstanceName()
        {
            return _context.ReplicaOrInstanceId.ToString();
        }
    }
}
