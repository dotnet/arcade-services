// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
using System;

namespace Microsoft.DotNet.DarcLib.Models;

public class Review
{

    public Review(ReviewState state, string url)
    {
        Status = state;
        Url = url;
    }
    public Review(ReviewState state, string url, string userLogin, DateTimeOffset submittedAt)
    {
        Status = state;
        Url = url;
        UserLogin = userLogin;
        SubmittedAt = submittedAt;
    }

    public ReviewState Status { get; }
    public string Url { get; }

    public string UserLogin {  get; }
    public DateTimeOffset SubmittedAt { get; }
}
