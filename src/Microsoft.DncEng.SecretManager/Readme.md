# The .NET Core Engineering Secret Manager
This tool is used to track and rotate the secrets in one or more key vaults, and to validate the usages of the same. This is done through a yaml manifest file for each vault, and json settings files for each service.

## Manifest Files
A manifest file describes the expected state of a single secret store (currently only Azure Key Vault, but support for other stores can be added easily). The manifest contains an inventory of all secrets and the information required to generate/rotate them. The format is as follows.
```yaml
# The destination secret store. All the secrets described in this document will be placed in here.
storageLocation:
  type: azure-key-vault
  parameters:
    name: helixservice
    subscription: <a guid>

# A collection of named references to other secret stores, secrets in this document can reference values from these stores if they are required
references:
  helix-admin:
    type: azure-key-vault
    parameters:
      name: helixadmin
      subscription: <a guid>


# encryption keys that are needed in the vault, these keys will be created if they don't exist.
keys:
  data-protection-encryption-key:
    type: RSA
    size: 2048

# each entry below is a separate secret that will be maintained in the secret store
# the type identifies what kind of secret it is, and the parameters drive the creation of the secret
secrets:
  # this secret is a blob sas uri created for an account using a connection string
  # in the 'helix-admin' vault
  secret-key-blob-uri:
    type: azure-storage-blob-sas-uri
    parameters:
      # This is a 'SecretReference' parameter. Some secrets require other secrets to work.
      # These parameters can be specified as a single string, or with explicit name and location
      # If a string is used, the referenced secret is assumed to be in the same vault as the current secret
      # location can be any of the entries in the 'references' collection
      # this specific entry references a secret in the reference named 'helix-admin'
      connectionString:
        name: execution-storage-connection-string
        location: helix-admin
      blob: keys.xml
      container: dp
      permissions: racwd
  
  execution-storage-connection:
    type: azure-storage-connection-string
    parameters:
      subscription: <a guid>
      account: helixexecution
  
  execution-storage-container-sas:
    type: azure-storage-container-sas-uri
    parameters:
      # This is also a 'SecretReference' but it has no location, this references the 'execution-storage-connection' secret contained in this document.
      connectionString: execution-storage-connection
      container: logs
      permissions: racwd
```

## Settings Files
Each service has at least one settings file. These are expected to be named `settings.json` with additional "environment" files alongside it named `settings.<environment>.json`. These are a simple json file with settings defined inside it. The values in this file should contain references to secrets using the `[vault(secret-name)]` syntax. An example is below.
```json
{
  "BuildAssetRegistry": {
    "ConnectionString": "[vault(build-asset-registry-sql-connection-string)]",
    "CacheSize": 40
  },
  "Storage": {
    "str": "[vault(storage-connection)]",
    "blobSas": "[vault(blob-sas)]",
    "containerSas": "[vault(container-sas)]",
    "tableSas": "[vault(table-sas)]",
    "UseOldAlgorithm": false
  },
  "EventHub": "[vault(event-hub-connection)]",
  "ServiceBus": "[vault(service-bus-connection)]",
  "Sql":{
    "admin": "[vault(sql-a-connection)]",
    "read": "[vault(sql-r-connection)]",
    "write": "[vault(sql-w-connection)]",
    "rw": "[vault(sql-rw-connection)]"
  },
  "EnableSuperSecretFeature": true
}
```

## Functionality in the Builds (FR Look Here)
The Secret Manager is set up to run 3 separate parts in our builds: usage validation during the testing phase, vault validation before deployment, and secret rotation on a weekly cadence.

### Usage Validation
This step runs the `validate-all` command. This command finds all `settings.json` and `settings.<environment>.json` files and validates those files with the manifests in the repo. This step doesn't talk to azure at all, it simply validates that all `[vault()]` references in the settings files have matching specifications in the appropriate manifest. Failures in this step require either adding missing definitions to the manifest, or removing the offending references in the settings file.

### Pre-Deployment Validation
This step runs the `synchronize --verify-only` command before the "approval" stage for the deployments. This command compares each manifest with the contents of the corresponding vault, and produces errors if secrets in the vault are missing or out-of-date. Failures in this step can be fixed by running the weekly build again, or by manually running the `synchronize` command.

### Weekly Rotation
This step runs the `synchronize` command. This compares every secret specified in the manifest with the corresponding vault, and performs the appropriate rotation for each secret that requires it. Failures in this step are caused by secrets that require rotation and can't be rotated automatically. To manually rotate these secrets a human will need to run the `synchronize` command manually and specify the manifest that contains the secret requiring rotation.

## Rotating Secrets On-Demand
Secrets can be rotated on-demand with the synchronize command. Just pass `--force` to rotate all secrets in the manifest, or `--force-secret=<secretName>` one or more times to rotate a specific secret or secrets.

## Examples
These must be run in a context that can resolve the .net cli tool secret-manager, any repo that is onboarded to secret manager will have this set up. The tool can also be installed globally if that is prefered.

### Rotate Every Secret in a manifest
    dotnet secret-manager synchronize --force <manifest.yaml>

### Rotate a single secret
    dotnet secret-manager synchronize --force-secret=<secret-name> <manifest.yaml>

## Onboarding a new Repo
- If .config/dotnet-tools.json doesn't exist, run `dotnet new tool-manifest`
- Run `dotnet tool install microsoft.dnceng.secretmanager --version 1.1.0-*`
- Create manifests for vaults in `$(RepoRoot)/.vault-config`. The path is arbitrary, but must match the scripts below.
- Add the following stage to a weekly build:
```yaml
  - stage: SynchronizeSecrets
    jobs:
    - job: Synchronize
      pool:
        vmImage: windows-2019
      steps:
      - task: UseDotNet@2
        displayName: Install Correct .NET Version
        inputs:
          useGlobalJson: true

      - task: UseDotNet@2
        displayName: Install .NET 3.1 runtime
        inputs:
          packageType: runtime
          version: 3.1.x

      - script: dotnet tool restore

      - task: AzureCLI@2
        inputs:
          azureSubscription: DotNet Eng Services Secret Manager
          scriptType: ps
          scriptLocation: inlineScript
          inlineScript: |
            Get-ChildItem .vault-config/*.yaml |% { dotnet secret-manager synchronize $_}
```
- Add the following steps immediately after the compilation for PR and CI builds:
```yaml
- script: dotnet tool restore

- powershell: |
    $manifestArgs = @()
    Get-ChildItem .vault-config/*.yaml |% {
      $manifestArgs += @("-m", $_.FullName)
    }
    dotnet secret-manager validate-all -b src @manifestArgs
  displayName: Verify Secret Usages
```
- Add the following stage before the "approval" stage that precedes deployment:
```yaml
- stage: ValidateSecrets
  dependsOn:
  - Build
  jobs:
  - job: ValidateSecrets
    pool:
      vmImage: windows-2019
    steps:
    - task: UseDotNet@2
      displayName: Install Correct .NET Version
      inputs:
        useGlobalJson: true

    - task: UseDotNet@2
      displayName: Install .NET 3.1 runtime
      inputs:
        packageType: runtime
        version: 3.1.x

    - script: dotnet tool restore

    - task: AzureCLI@2
      inputs:
        azureSubscription: DotNet Eng Services Secret Manager
        scriptType: ps
        scriptLocation: inlineScript
        inlineScript: |
          Get-ChildItem .vault-config/*.yaml |% { dotnet secret-manager synchronize --verify-only $_}
```


## Creating new secrets
New secrets can be created by adding new entries to the manifest file, then running synchronize. The tool will then create the new secret and add it to the secret store.

## Secret Types
Each secret in the manifest has a specified type, and these types determine what parameters are required, and what ends up in the secret store. Some secret types require human interaction to generate and/or rotate. For such secrets the tool will prompt the user or fail if in a build context. Some secret types also produce multiple values. These values are all stored with additional suffixes in the secret store. Each type is documented below.

### Azure Storage

#### Storage Key
Only use this secret type if the connection string is required in multiple vaults. The storage key should be in one of them, and each vault can have a connection string secret that references the key.
```yaml
type: azure-storage-key
parameters:
  account: storage account name
  subscription: Azure subscription id
```

#### Connection String
```yaml
type: azure-storage-connection-string
parameters:
  account: storage account name
  # one of the following is required
  storageKeySecret: SecretReference to an azure-storage-key
  subscription: Azure subscription id
```

#### Account Sas Token
```yaml
type: azure-storage-account-sas-token
parameters:
  connectionString: SecretReference to the connection string for the account
  service: Service i.e (blob, queue, table, file) that the token is for. Can specify more than one by separating them with |, i.e (blob|queue)
  permissions: permissions needed for the sas e.g. 'racwd'
```

#### Container Sas Uri
```yaml
type: azure-storage-container-sas-uri
parameters:
  connectionString: SecretReference to the connection string for the account
  container: storage container name
  permissions: permissions needed for the sas e.g. 'racwd'
```

#### Container Sas Token
```yaml
type: azure-storage-container-sas-token
parameters:
  connectionString: SecretReference to the connection string for the account
  container: storage container name
  permissions: permissions needed for the sas e.g. 'racwd'
```

#### Blob Sas Uri
```yaml
type: azure-storage-blob-sas-uri
parameters:
  connectionString: SecretReference to the connection string for the account
  container: storage container name
  blob: blob name
  permissions: permissions needed for the sas e.g. 'racwd'
```

#### Table Sas Uri
```yaml
type: azure-storage-table-sas-uri
parameters:
  connectionString: SecretReference to the connection string for the account
  table: storage table name
  permissions: permissions needed for the sas e.g. 'racwd'
```

### Event Hub Connection String
```yaml
type: event-hub-connection-string
parameters:
  subscription: Azure subscription id
  resourceGroup: Azure resource group
  namespace: Event Hub namespace
  name: Event Hub name
  permissions: required permissions
```

### Random Base64
This type produces 2 values `<name>-primary` and `<name>-secondary`. The service must use both values to validate payloads, but only encode things with the primary value.
```yaml
type: random-base64
parameters:
  bytes: integer
```

### Service Bus Connection String
```yaml
type: service-bus-connection-string
parameters:
  subscription: Azure subscription id
  resourceGroup: Azure resource group
  namespace: Service Bus namespace
  permissions: required permissions
```

### Sql Connection String
```yaml
type: sql-connection-string
parameters:
  adminConnection: secret reference to the sql admin connection
  dataSource: server url
  database: database name
  permissions: 'admin', 'r', 'w', or 'rw'
```

### Kusto Connection String
```yaml
type: kusto-connection-string
parameters:
  dataSource: the DataSource in the connection string
  initialCatalog: the InitialCatalog in the connection string
  additionalParameters: and extra 
  adApplication: SecretReference to the ad-application used for authentication
```

### GitHub access token
```yaml
type: github-access-token
parameters:
  gitHubBotAccountSecret: secret reference to the GitHub account this token is for
  gitHubBotAccountName: username of the GitHub account
```

### GitHub Application Secret
This type produces 5 values: `<name>-app-id`, `<name>-app-private-key`, `<name>-oauth-id`, `<name>-oauth-secret`, and `<name>-app-webhook-secret`.
```yaml
type: github-app-secret
parameters:
  hasPrivateKey: boolean
  hasWebHookSecret: boolean
  hasAppSecret: boolean
```

### GitHub Bot Account
This type produces 3 values: `<name>-password`, `<name>-secret`, and `<name>-recovery-codes`.
```yaml
type: github-account
parameters:
  name: the account name
```

### GitHub OAuth app
This type produces 2 values: `<name>-client-id`, and `<name>-client-secret`.
```yaml
type: github-oauth-secret
parameters:
  appName: name of application
  description: description
```

### Grafana Api Key
```yaml
type: grafana-api-key
parameters:
  environment: hostname of target grafana instance
```

### Helix Access Token
```yaml
type: helix-access-token
parameters:
  environment: hostname of target helix instance
```

### Maestro Access Token
```yaml
type: maestro-access-token
parameters:
  environment: hostname of target maestro++ instance
```

### Text
This type should be used sparingly, and only for things that aren't actually secret.
```yaml
type: text
parameters:
  description: what the text is
```

### Domain Account
```yaml
type: domain-account
parameters:
  accountName: full account name including domain
  description: additional description for rotation of the password
```

### Azure Active Directory Application
Produces `<name>-app-id` and `<name>-app-secret`
```yaml
type: ad-application
```

### Azure DevOps Access Token
```yaml
type: azure-devops-access-token
parameters:
  organizations: space separate list of organizations
  scopes: space separated list of scopes in the format accepted by pat-generator
  domainAccountName: name of domain account
  domainAccountSecret: secret reference to a domain-account
```

### Base64 Encode
This type base64 encodes the referenced secret.
```yaml
type: base64-encoder
parameters:
  secret: SecretReference to another secret
```
