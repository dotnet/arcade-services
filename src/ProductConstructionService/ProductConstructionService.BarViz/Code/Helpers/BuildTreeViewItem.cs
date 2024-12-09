// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.DotNet.ProductConstructionService.Client.Models;

namespace ProductConstructionService.BarViz.Code.Helpers;

public class BuildTreeViewItem : ITreeViewItem
{
    public string Id { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public IEnumerable<ITreeViewItem>? Items { get; set; }
    public Icon? IconCollapsed { get; set; }
    public Icon? IconExpanded { get; set; }
    public bool Disabled { get; set; }
    public bool Expanded { get; set; }
    public Func<TreeViewItemExpandedEventArgs, Task>? OnExpandedAsync { get; set; }
    public required Build Build { get; init; }
}
