// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#nullable enable
namespace Microsoft.DotNet.DarcLib.Helpers;

/// <summary>
/// Smart file path wrapper that allows easy combining and makes sure separators conform to the desired way.
/// They support concatenation where the left-most (root) path decides the shape of the outcome.
/// (e.g. "C:\foo" / "src/something" returns "C:\foo\src\something")
/// 
/// The concatenation works thanks to overloading of the / operator:
///     var combinedPath = rootPath / "src" / "MyProject.csproj";
/// </summary>
public abstract class FilePath
{
    private readonly char _separator;

    public string Path { get; }

    public int Length => Path.Length;

    public FilePath(string rootPath, char separator) : this(rootPath, separator, true)
    {
    }

    protected FilePath(string path, char separator, bool normalizePath)
    {
        Path = normalizePath ? NormalizePath(path) : path;
        _separator = separator;
    }

    public override string ToString() => Path;

    public static FilePath operator /(FilePath left, FilePath right) => left.CreateMergedPath(left.Combine(left.Path, left.NormalizePath(right.Path)));

    public static FilePath operator /(FilePath left, string right) => left.CreateMergedPath(left.Combine(left.Path, left.NormalizePath(right)));

    public static FilePath operator /(string left, FilePath right) => right.CreateMergedPath(right.Combine(right.NormalizePath(left), right.Path));

    public static implicit operator string(FilePath p) => p.Path;

    protected abstract FilePath CreateMergedPath(string path);

    protected abstract string NormalizePath(string s);

    private string Combine(string left, string right)
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

    public override bool Equals(object? obj) => Path.Equals((obj as FilePath)?.Path ?? obj as string);

    public override int GetHashCode() => Path.GetHashCode();
}

/// <summary>
/// Smart path that defaults to using whatever is the current directory separator.
/// </summary>
public class NativePath : FilePath
{
    public NativePath(string rootPath) : this(rootPath, true)
    {
    }

    private NativePath(string rootPath, bool normalize) : base(rootPath, System.IO.Path.DirectorySeparatorChar, normalize)
    {
    }

    protected override FilePath CreateMergedPath(string path) => new NativePath(path, false);

    protected override string NormalizePath(string s) =>
        System.IO.Path.DirectorySeparatorChar == '/'
            ? s.Replace('\\', '/')
            : s.Replace('/', '\\');
}

/// <summary>
/// Smart path that uses the UNIX style (forward-slashes) for directory separation.
/// </summary>
public class UnixPath : FilePath
{
    public UnixPath(string rootPath) : this(rootPath, true)
    {
    }

    private UnixPath(string rootPath, bool normalize) : base(rootPath, '/', normalize)
    {
    }

    protected override FilePath CreateMergedPath(string path) => new UnixPath(path, false);

    protected override string NormalizePath(string s) => s.Replace('\\', '/');
}

/// <summary>
/// Smart path that uses the Windows style (back-slashes) for directory separation.
/// </summary>
public class WindowsPath : FilePath
{
    public WindowsPath(string rootPath) : this(rootPath, true)
    {
    }

    private WindowsPath(string rootPath, bool normalize) : base(rootPath, '\\', normalize)
    {
    }

    protected override FilePath CreateMergedPath(string path) => new WindowsPath(path, false);

    protected override string NormalizePath(string s) => s.Replace('/', '\\');
}
