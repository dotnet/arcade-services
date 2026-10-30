// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.CommandLine;

namespace Microsoft.DotNet.Darc.Options;

/// <summary>
/// A single CLI symbol (an option or a positional argument) paired with the code that copies its
/// parsed value onto the options instance that declared it.
/// </summary>
/// <param name="Symbol">The symbol presented to the user.</param>
/// <param name="Separator">Character that splits one token into several values, when the symbol accepts a separated list.</param>
/// <param name="Required">Whether the user has to supply the symbol.</param>
/// <param name="BindingExpression">
/// Source text of the setter lambda, captured by the compiler. The CLI surface test uses it to assert
/// that every option still writes to the same property it wrote to before.
/// </param>
/// <param name="Bind">Copies the parsed value onto the declaring options instance.</param>
internal sealed record CliSymbol(
    Symbol Symbol,
    char? Separator,
    bool Required,
    string BindingExpression,
    Action<ParseResult> Bind);
