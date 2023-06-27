// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace SubscriptionActorService;

[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public class ActionMethodAttribute : Attribute
{
    public ActionMethodAttribute(string format)
    {
        Format = format;
    }

    public string Format { get; }
}
