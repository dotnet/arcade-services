using System;
using Microsoft.Rest.TransientFaultHandling;

namespace Microsoft.DncEng.DeployServiceFabricCluster
{
    internal class DefaultTransientErrorDetectionStrategy : ITransientErrorDetectionStrategy
    {
        private readonly ITransientErrorDetectionStrategy _inner = new HttpStatusCodeErrorDetectionStrategy();

        public bool IsTransient(Exception ex)
        {
            if (_inner.IsTransient(ex))
            {
                return true;
            }
            return false;
        }
    }
}
