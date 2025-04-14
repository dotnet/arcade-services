// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace FlatFlowMigrationCli.Operations;

internal interface IOperation
{
    Task<int> RunAsync();
}

internal abstract class Operation : IOperation
{
    public abstract Task<int> RunAsync();

    protected static void ConfirmOperation(string message)
    {
        Console.Write($"{message} (y/N)? ");
        var key = Console.ReadKey(intercept: false);
        Console.WriteLine();
        if (key.KeyChar != 'y' && key.KeyChar != 'Y')
        {
            Environment.Exit(0);
        }
    }
}
