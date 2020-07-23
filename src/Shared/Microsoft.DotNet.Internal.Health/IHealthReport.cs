// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Threading.Tasks;
using JetBrains.Annotations;

namespace Microsoft.DotNet.Internal.Health
{
    public interface IHealthReport
    {
        [PublicAPI]
        Task UpdateStatus(string subStatusName, HealthStatus status, string message);
    }

    /// <summary>
    /// An injectable service health reporter for reporting status of a service
    /// </summary>
    /// <typeparam name="TService">Type of the service health is being reported for, the full name will be used as the name in reports</typeparam>
    public interface IHealthReport<[UsedImplicitly] TService> : IHealthReport
    {
    }
}
