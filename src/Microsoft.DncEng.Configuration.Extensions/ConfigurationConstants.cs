namespace Microsoft.DncEng.Configuration.Extensions
{
    public static class ConfigurationConstants
    {
        public static string KeyVaultUriConfigurationKey => "KeyVaultUri";
        public static string AppConfigurationUriConfigurationKey => "AppConfigurationUri";
        public static string ManagedIdentityIdConfigurationKey => "Secrets:ManagedIdentityId";
        public static string ReloadTimeSecondsConfigurationKey => "Secrets:ReloadTimeSeconds";
    }
}
