// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using System.Text;
using System.Xml;
using Newtonsoft.Json.Linq;
using Formatting = Newtonsoft.Json.Formatting;

namespace Microsoft.DotNet.DarcLib
{
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

        private static string GetIndentedXmlBody(XmlDocument xmlDocument)
        {
            MemoryStream mStream = new MemoryStream();
            XmlTextWriter writer = new XmlTextWriter(mStream, Encoding.Unicode);
            XmlDocument document = new XmlDocument();

            try
            {
                document.LoadXml(xmlDocument.OuterXml);
                writer.Formatting = System.Xml.Formatting.Indented;
                document.WriteContentTo(writer);
                writer.Flush();
                mStream.Flush();
                mStream.Position = 0;

                StreamReader sReader = new StreamReader(mStream);

                return sReader.ReadToEnd();
            }
            catch (XmlException)
            {
                throw;
            }
        }
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
}
