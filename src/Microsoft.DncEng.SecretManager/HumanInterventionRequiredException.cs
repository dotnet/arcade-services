using System;

namespace Microsoft.DncEng.SecretManager;

public class HumanInterventionRequiredException : Exception
{
    public HumanInterventionRequiredException(string message)
        :base(message)
    {
    }
}
