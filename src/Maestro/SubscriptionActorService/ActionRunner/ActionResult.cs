// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace SubscriptionActorService;

public static class ActionResult
{
    public static ActionResult<T> Create<T>(T result, string message)
    {
        return new ActionResult<T>(result, message);
    }

    public static ActionResult<object> Create(string message)
    {
        return new ActionResult<object>(null, message);
    }
}

public class ActionResult<T>
{
    public ActionResult(T result, string message)
    {
        Result = result;
        Message = message;
    }

    public T Result { get; }
    public string Message { get; }
}
