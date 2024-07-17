// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;

#nullable enable
namespace Microsoft.DotNet.Darc.Operations;

public abstract class Operation
{
    protected readonly IBarApiClient _barClient;

    public Operation(IBarApiClient barClient) => _barClient = barClient;

    public abstract Task<int> ExecuteAsync();

    /// <summary>
    ///  Indicates whether the requested output format is supported.
    /// </summary>
    /// <param name="outputFormat">The desired output format.</param>
    /// <returns>
    ///  The base implementations returns <see langword="true"/> for <see cref="DarcOutputType.text"/>; otherwise <see langword="false"/>.
    /// </returns>
    protected virtual bool IsOutputFormatSupported(DarcOutputType outputFormat)
        => outputFormat switch
        {
            DarcOutputType.text => true,
            _ => false
        };
}
