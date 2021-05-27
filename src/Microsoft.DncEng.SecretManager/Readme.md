# The .NET Core Engineering Secret Manager
This tool is used to track and rotate the secrets in one or more key vaults, and to validate the usages of the same. This is done through a yaml manifest file for each vault, and json settings files for each service.

## Rotating secrets
The synchronize command can be used to rotate secrets that need rotation. It will automatically rotate all secrets that have either expired or hit their rotation time. This command is run as part of a scheduled build to keep all secrets up to date. Any secrets that can be rotated without a human involved will be rotated automatically. For secrets that require human interaction, the build will fail and a human will have to run synchronize.

## Creating new secrets
New secrets can be created by adding new entries to the manifest file, then running synchronize. The tool will then create the new secret and add it to the secret store.

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

## Secret Types
Each secret in the manifest has a specified type, and these types determine what parameters are required, and what ends up in the secret store. Some secret types require human interaction to generate and/or rotate. For such secrets the tool will prompt the user or fail if in a build context. Some secret types also produce multiple values. These values are all stored with additional suffixes in the secret store. Each type is documented below.

### Azure Storage

#### Connection String
```yaml
type: azure-storage-connection-string
parameters:
  subscription: Azure subscription id
  account: storage account name
```

#### Container Sas Uri
```yaml
type: azure-storage-container-sas-uri
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

### GitHub access token
```yaml
type: github-access-token
parameters:
  gitHubBotAccountSecret: secret reference to the GitHub account this token is for
  gitHubBotAccountName: username of the GitHub account
```

### GitHub Application Secret
This type produces 4 values: `<name>-app-id`, `<name>-app-private-key`, `<name>-app-secret`, and `<name>-app-hook`.
```yaml
type: github-app-secret
parameters:
  appName: name of GitHub app
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
```

### Grafana Api Key
```yaml
type: grafana-api-key
parameters:
  environment: staging or production
```

### Helix Access Token
```yaml
type: helix-access-token
parameters:
  environment: staging or production
```

### Maestro Access Token
```yaml
type: maestro-access-token
parameters:
  environment: staging or production
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
  domain: domain name
  user: user account name
  description: additional description for rotation of the password
```
