// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace ProductConstructionService.BarViz.Code;

public enum SelectedDependencyRelationType
{
    None,
    Selected,
    Parent,
    Child,
    Ancestor,
    Descendant,
    Conflict
}
