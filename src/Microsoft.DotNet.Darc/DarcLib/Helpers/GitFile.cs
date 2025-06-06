// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using Newtonsoft.Json.Linq;
using Formatting = Newtonsoft.Json.Formatting;

namespace Microsoft.DotNet.DarcLib.Helpers;

public class GitFile
{
    public GitFile(string filePath, XmlDocument xmlDocument) : this(filePath, GetIndentedXmlBody(xmlDocument))
    {
    }

    public GitFile(string filePath, JObject jsonObject) : this(filePath, jsonObject.ToString(Formatting.Indented))
    {
    }

    public GitFile(string filePath, string content) : this(filePath, content, ContentEncoding.Utf8)
    {
    }

    public GitFile(string filePath, JObject jsonObject, Dictionary<GitFileMetadataName, string> metadata)
        : this(filePath, jsonObject.ToString(Formatting.Indented))
    {
        Metadata = metadata;
    }

    public GitFile(
        string filePath,
        string content,
        ContentEncoding contentEncoding,
        string mode = "100644",
        GitFileOperation operation = GitFileOperation.Add)
    {
        FilePath = filePath;
        // TODO: Newline normalization should happen on the writer side,
        // since the writer knows the local repo/remote repo context.
        Content = content.Replace(Environment.NewLine, "\n");
        // Ensure it ends in a newline
        if (!Content.EndsWith("\n"))
        {
            Content = $"{Content}\n";
        }
        ContentEncoding = contentEncoding;
        Mode = mode;
        Operation = operation;
    }

    public string FilePath { get; }

    public string Content { get; }

    public ContentEncoding ContentEncoding { get; }

    public string Mode { get; } = "100644";

    public GitFileOperation Operation { get; } = GitFileOperation.Add;

    public Dictionary<GitFileMetadataName, string> Metadata { get; set; }

    public const string GitDirectory = ".git";

    private static string GetIndentedXmlBody(XmlDocument xmlDocument)
    {
        var mStream = new MemoryStream();
        var writer = new XmlTextWriter(mStream, Encoding.Unicode);
        var document = new XmlDocument();

        try
        {
            document.LoadXml(xmlDocument.OuterXml);
            writer.Formatting = System.Xml.Formatting.Indented;
            document.WriteContentTo(writer);
            writer.Flush();
            mStream.Flush();
            mStream.Position = 0;

            var sReader = new StreamReader(mStream);

            return sReader.ReadToEnd();
        }
        catch (XmlException)
        {
            throw;
        }
    }

    public static void MakeGitFilesDeletable(string path)
    {
        var gitFiles = Directory.GetDirectories(path, GitDirectory, SearchOption.AllDirectories)
                    .Select(gitDir => Path.Combine(gitDir, "objects"))
                    .SelectMany(q => Directory.GetFiles(q, "*", SearchOption.AllDirectories));
        foreach (var gitFile in gitFiles)
        {
            var fileInfo = new FileInfo(gitFile);
            fileInfo.Attributes = FileAttributes.Normal;
            fileInfo.IsReadOnly = false;
        }
    }
}

public enum GitFileMetadataName
{
    // Used when the GitFile contains an update to global.json -> tools.dotnet entry.
    ToolsDotNetUpdate,

    // Used when the GitFile contains an update to global.json -> sdk.version entry.
    SdkVersionUpdate
}

public enum GitFileOperation
{
    Add,
    Delete
}

public enum ContentEncoding
{
    Base64,
    Utf8
}
