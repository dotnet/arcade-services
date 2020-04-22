namespace Microsoft.DncEng.Configuration.Extensions
{
    public static class ConfigurationConstants
    {
        public static string KeyVaultUriConfigurationKey => "KeyVaultUri";
        public static string AppConfigurationUriConfigurationKey => "AppConfigurationUri";
        public static string ManagedIdentityIdConfigurationKey => "Secrets:ManagedIdentityId";
        public static string ReloadTimeSecondsConfigurationKey => "Secrets:ReloadTimeSeconds";

        public static string MsftAdTenantId => "72f988bf-86f1-41af-91ab-2d7cd011db47";

        // This group name must be kept in sync with 2 other places
        // - The ApplicationManifest.xml for the MaestroApplication service fabric
        // - The bootstrap.ps1 script, where this group gets created
        public static string ConfigurationAccessGroupName = "DncEngConfigurationUsers";
    }
}
