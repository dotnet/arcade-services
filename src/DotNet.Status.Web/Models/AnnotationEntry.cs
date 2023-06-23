namespace DotNet.Status.Web.Models;

public class AnnotationEntry
{
    /// <summary>
    /// The original annotation sent from Grafana.
    /// </summary>
    public Annotation Annotation { get; set; }
    /// <summary>
    /// Time since UNIX Epoch in milliseconds. (required)
    /// </summary>
    public long Time { get; set; }
    public long? TimeEnd { get; set; }
    public bool? IsRange { get; set; } = false;
    /// <summary>
    /// The title for the annotation tooltip. (required)
    /// </summary>
    public string Title { get; set; }
    /// <summary>
    /// Tags for the annotation. (optional)
    /// </summary>
    public string[] Tags { get; set; }
    /// <summary>
    /// Text for the annotation. (optional)
    /// </summary>
    public string Text { get; set; }

    public AnnotationEntry(Annotation annotation, long time, string title)
    {
        Annotation = annotation;
        Time = time;
        Title = title;
    }
}
