// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json.Serialization;

#nullable disable
namespace BuildInsights.BuildAnalysis.Models;

public class ArtifactDetailsFile
{
    [JsonPropertyName("items")]
    public List<Item> Items { get; set; }
}

public class Item
{
    [JsonPropertyName("path")]
    public string Path { get; set; }

    [JsonPropertyName("blob")]
    public Blob Blob { get; set; }

}

public class Blob
{
    [JsonPropertyName("id")]
    public string Id { get; set; }

    [JsonPropertyName("size")]
    public int Size { get; set; }
}
