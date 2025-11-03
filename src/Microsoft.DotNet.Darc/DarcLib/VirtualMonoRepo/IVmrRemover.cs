// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public interface IVmrRemover
{
    /// <summary>
    /// Removes a repository from the VMR.
    /// </summary>
    /// <param name="mappingName">Name of a repository mapping</param>
    /// <param name="codeFlowParameters">Record containing parameters for VMR operations</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task RemoveRepository(
        string mappingName,
        CodeFlowParameters codeFlowParameters,
        CancellationToken cancellationToken);
}
