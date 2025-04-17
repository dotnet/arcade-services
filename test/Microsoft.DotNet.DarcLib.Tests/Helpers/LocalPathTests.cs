// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.IO;
using Shouldly;
using Microsoft.DotNet.DarcLib.Helpers;
using NUnit.Framework;

namespace Microsoft.DotNet.DarcLib.Tests.Helpers;

[TestFixture]
public class LocalPathTests
{
    [Test]
    public void UnixStylePathsCombineWell()
    {
        var path1 = new UnixPath("src/");
        var path2 = new UnixPath("/some/path/foo.jpg");

        path1.Path.ShouldBe("src/");
        path2.Path.ShouldBe("/some/path/foo.jpg");
        (path1 / path2).Path.ShouldBe("src/some/path/foo.jpg");
        (path2 / path1).Path.ShouldBe("/some/path/foo.jpg/src/");
        (path1 / "/something/else").Path.ShouldBe("src/something/else");
        ("/something/else" / path1).Path.ShouldBe("/something/else/src/");
        (path1 / "something\\else").Path.ShouldBe("src/something/else");
        new UnixPath("something\\else").Path.ShouldBe("something/else");
    }

    [Test]
    public void WindowsStylePathsCombineWell()
    {
        var path1 = new WindowsPath("D:\\foo\\bar");
        var path2 = new WindowsPath("some/path/foo.jpg");

        path1.Path.ShouldBe("D:\\foo\\bar");
        path2.Path.ShouldBe("some\\path\\foo.jpg");
        (path1 / path2).Path.ShouldBe("D:\\foo\\bar\\some\\path\\foo.jpg");
        (path2 / path1).Path.ShouldBe("some\\path\\foo.jpg\\D:\\foo\\bar");
        (path1 / "/something/else").Path.ShouldBe("D:\\foo\\bar\\something\\else");
        ("something/else" / path1).Path.ShouldBe("something\\else\\D:\\foo\\bar");
    }

    [Test]
    public void NativeStylePathsCombineWell()
    {
        var path1 = new NativePath("foo\\bar\\");
        var path2 = new NativePath("some/path/foo.jpg");

        (path1 / path2).Path.ShouldBe(
            Path.Combine(
                path1.Path.Replace('\\', Path.DirectorySeparatorChar),
                path2.Path.Replace('/', Path.DirectorySeparatorChar)));
    }
}
