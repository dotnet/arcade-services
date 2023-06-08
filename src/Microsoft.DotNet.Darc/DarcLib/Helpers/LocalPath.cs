// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable enable
namespace Microsoft.DotNet.DarcLib.Helpers;

/// <summary>
/// Smart file path wrapper that allows easy combining and makes sure separators conform to the desired way.
/// They support concatenation where the left-most path decides the shape of the outcome.
/// (e.g. "C:\foo" / "src/something" returns "C:\foo\src\something")
/// 
/// The concatenation works thanks to overloading of the / operator:
///     var combinedPath = someLocalPath / "src" / "MyProject.csproj";
/// </summary>
public abstract class LocalPath
{
    private readonly char _separator;

    public string Path { get; }

    public int Length => Path.Length;

    protected LocalPath(string path, char separator) : this(path, separator, true)
    {
    }

    protected LocalPath(string path, char separator, bool normalizePath)
    {
        Path = normalizePath ? NormalizePath(path) : path;
        _separator = separator;
    }

    public override string ToString() => Path;

    public static LocalPath operator /(LocalPath left, LocalPath right)
        => left.CreateMergedPath(left.Combine(left.Path, left.NormalizePath(right.Path)));

    public static LocalPath operator /(LocalPath left, string right)
        => left.CreateMergedPath(left.Combine(left.Path, left.NormalizePath(right)));

    public static LocalPath operator /(string left, LocalPath right)
        => right.CreateMergedPath(right.Combine(right.NormalizePath(left), right.Path));

    public static implicit operator string(LocalPath p) => p.Path;

    protected abstract LocalPath CreateMergedPath(string path);

    protected abstract string NormalizePath(string s);

    protected string Combine(string left, string right)
    {
        var slashCount = (left.EndsWith(_separator) ? 1 : 0) + (right.StartsWith(_separator) ? 1 : 0);

        return slashCount switch
        {
            0 => left + _separator + right,
            1 => left + right,
            2 => left + right[1..],
            _ => throw new System.NotImplementedException(),
        };
    }

    public override bool Equals(object? obj) => Path.Equals((obj as LocalPath)?.Path ?? obj as string);

    public override int GetHashCode() => Path.GetHashCode();
}

/// <summary>
/// Smart path that defaults to using whatever is the current directory separator.
/// </summary>
public class NativePath : LocalPath
{
    public NativePath(string path) : this(path, true)
    {
    }

    private NativePath(string path, bool normalize) : base(path, System.IO.Path.DirectorySeparatorChar, normalize)
    {
    }

    public static NativePath operator /(NativePath left, string right)
        => new(left.Combine(left.Path, left.NormalizePath(right)), false);

    public static NativePath operator /(NativePath left, LocalPath right)
        => new(left.Combine(left.Path, left.NormalizePath(right)), false);

    protected override LocalPath CreateMergedPath(string path) => new NativePath(path, false);

    protected override string NormalizePath(string s)
        => System.IO.Path.DirectorySeparatorChar == '/' ? s.Replace('\\', '/') : s.Replace('/', '\\');
}

/// <summary>
/// Smart path that uses the UNIX style (forward-slashes) for directory separation.
/// </summary>
public class UnixPath : LocalPath
{
    public UnixPath(string path) : this(path, true)
    {
    }

    private UnixPath(string path, bool normalize) : base(path, '/', normalize)
    {
    }

    public static UnixPath operator /(UnixPath left, string right)
        => new(left.Combine(left.Path, left.NormalizePath(right)), false);

    protected override LocalPath CreateMergedPath(string path) => new UnixPath(path, false);

    protected override string NormalizePath(string s) => s.Replace('\\', '/');
}

/// <summary>
/// Smart path that uses the Windows style (back-slashes) for directory separation.
/// </summary>
public class WindowsPath : LocalPath
{
    public WindowsPath(string path) : this(path, true)
    {
    }

    private WindowsPath(string path, bool normalize) : base(path, '\\', normalize)
    {
    }

    public static WindowsPath operator /(WindowsPath left, string right)
        => new(left.Combine(left.Path, left.NormalizePath(right)), false);

    protected override LocalPath CreateMergedPath(string path) => new WindowsPath(path, false);

    protected override string NormalizePath(string s) => s.Replace('/', '\\');
}
