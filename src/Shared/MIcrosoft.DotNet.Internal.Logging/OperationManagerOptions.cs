namespace Microsoft.DotNet.Internal.Logging
{
    public class OperationManagerOptions
    {
        public bool ShouldStartActivity { get; set; } = true;
        public bool ShouldCreateLoggingScope { get; set; } = true;
    }
}
