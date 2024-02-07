// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.ComponentModel.DataAnnotations;

namespace Maestro.Data.Models;

public class SubscriptionUpdate
{
    [Key]
    public Guid SubscriptionId { get; set; }

    public Subscription Subscription { get; set; }

    /// <summary>
    ///     **true** if the update succeeded; **false** otherwise.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    ///     A message describing what the subscription was trying to do.
    ///     e.g. 'Updating dependencies from dotnet/coreclr in dotnet/corefx'
    /// </summary>
    public string Action { get; set; }

    /// <summary>
    ///     The error that occured, if any.
    /// </summary>
    public string ErrorMessage { get; set; }

    /// <summary>
    ///     The method that was called.
    /// </summary>
    public string Method { get; set; }

    /// <summary>
    ///     The parameters to the called method.
    /// </summary>
    public string Arguments { get; set; }
}
