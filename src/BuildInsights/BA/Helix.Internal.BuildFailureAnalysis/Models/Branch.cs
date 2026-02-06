using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.Internal.Helix.BuildFailureAnalysis.Models
{
    public class BranchConverter : JsonConverter<Branch>
    {
        public override Branch Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return Branch.Parse(reader.GetString());
        }

        public override void Write(Utf8JsonWriter writer, Branch value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.BranchName);
        }
    }

    [JsonConverter(typeof(BranchConverter))]
    public class Branch : GitRef
    {
        internal const string HeadsNamespace = "refs/heads/";

        public string BranchName => Path[HeadsNamespace.Length..];

        internal Branch(string branchRef) : base(branchRef)
        {

        }

        public static new Branch Parse(string branchName)
        {
            if (string.IsNullOrEmpty(branchName))
            {
                throw new ArgumentNullException(nameof(branchName), "Branch name must not be null or empty");
            }

            return new Branch($"refs/heads/{branchName}");
        }
    }

    public class GitRef : IEquatable<GitRef>
    {
        public string Path { get; }

        protected GitRef(string path)
        {
            Path = path;
        }

        public bool Equals([AllowNull] GitRef other)
        {
            if (other == null)
                return false;

            return other.Path == Path;
        }

        public override bool Equals(object obj)
        {
            if (obj is null)
            {
                return false;
            }

            if (obj is GitRef gitRef)
            {
                return Equals(gitRef);
            }

            return false;
        }

        public override int GetHashCode()
        {
            return Path.GetHashCode();
        }

        public static GitRef Parse(string path)
        {
            if (path is null)
            {
                throw new ArgumentNullException(nameof(path), "Ref name must not be null");
            }

            if (!path.StartsWith("refs/"))
            {
                throw new FormatException("Ref must start from \"refs\" namespace");
            }

            if (path.StartsWith(Branch.HeadsNamespace))
            {
                return new Branch(path);
            }

            return new GitRef(path);
        }
    }
}
