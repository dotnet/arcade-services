namespace Microsoft.DncEng.CommandLineLib;

public class ExitCodes
{
    public const int Success = 0;
    public const int UnknownArgument = 1;
    public const int MissingCommand = 2;
    public const int RequiredParameter = 3;
    public const int UnhandledException = 4;
    public const int Break = 5;
}
