// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Tools.Cli.Core;

[AttributeUsage(AttributeTargets.Class)]
public class OperationAttribute : Attribute
{
    public Type OperationType { get; }

    public OperationAttribute(Type operationType)
    {
        if (!typeof(IOperation).IsAssignableFrom(operationType))
        {
            throw new ArgumentException(
                $"Type '{operationType.FullName}' does not implement {nameof(IOperation)}.",
                nameof(operationType));
        }

        OperationType = operationType;
    }
}

[AttributeUsage(AttributeTargets.Class)]
public class OperationAttribute<TOperation> : OperationAttribute
    where TOperation : IOperation
{
    public OperationAttribute() : base(typeof(TOperation))
    {
    }
}
