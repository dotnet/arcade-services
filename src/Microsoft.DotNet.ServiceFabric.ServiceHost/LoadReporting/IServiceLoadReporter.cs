namespace Microsoft.DotNet.ServiceFabric.ServiceHost
{
    /// <summary>
    ///   This type enables reporting load metrics to service fabric
    /// </summary>
    public interface IServiceLoadReporter
    {
        /// <summary>
        ///   Reports a service fabric load metric.
        /// </summary>
        /// <param name="name">The load metric name</param>
        /// <param name="value">The load metric value</param>
        void ReportLoad(string name, int value);
    }
}
