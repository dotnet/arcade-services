// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Threading.Tasks;
using Kusto.Data.Common;

namespace Microsoft.DotNet.Kusto
{
    public interface IKustoClientProvider
    {
        ICslQueryProvider GetKustoQueryConnectionProvider();
        string GetKustoDatabase();
    }
}