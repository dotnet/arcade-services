using System;

namespace DotNet.Status.Web.Models;

/// <summary>
/// Query body used by the Grafana SimpleJsonDataSource when requesting annotations
/// </summary>
public class AnnotationQueryBody
{
    public Range Range { get; set; }
    public RangeRaw RangeRaw { get; set; }
    public Annotation Annotation { get; set; }
}

public class Range
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
}

public class RangeRaw
{
    public string From { get; set; }
    public string To { get; set; }
}

public class Annotation
{
    public string Name { get; set; }
    public string Datasource { get; set; }
    public string IconColor { get; set; }
    public bool Enable { get; set; }
    public string Query { get; set; }
}
