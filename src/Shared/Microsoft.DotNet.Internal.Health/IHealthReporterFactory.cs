// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Microsoft.DotNet.Internal.Health
{
    public interface IHealthReporterFactory
    {
        IServiceHealthReporter<T> ForService<T>();
        IInstanceHealthReporter<T> ForInstance<T>();
        IExternalHealthReporter ForExternal(string serviceName);
        IExternalHealthReporter ForExternalInstance(string serviceName, string instance);
    }
}