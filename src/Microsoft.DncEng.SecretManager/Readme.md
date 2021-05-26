# The .NET Core Engineering Secret Manager
This tool is used to track and rotate the secrets in one or more key vaults, and to validate the usages of the same. This is done through a yaml manifest file for each vault, and json settings files for each service.

## Rotating secrets
The synchronize command can be used to rotate secrets that need rotation. It will automatically rotate all secrets that have either expired or hit their rotation time. This command is run as part of a scheduled build to keep all secrets up to date. Any secrets that can be rotated without a human involved will be rotated automatically. For secrets that require human interaction, the build will fail and a human will have to run synchronize.

## Creating new secrets
New secrets can be created by adding new entries to the manifest file, then running synchronize. The tool will then create the new secret and add it to the secret store.

## Manifest Files
A manifest file describes the expected state of a single secret store (currently only azure key vault, but support for other stores can be added easily). The manifest contains an inventory of all secrets and the information required to generate/rotate them. The format is as follows.
```yaml
# The destination secret store. All the secrets described in this document will be placed in here.
storageLocation:
  type: azure-key-vault
  parameters:
    name: <a string>
    subscription: <a guid>

# A collection of named references to other secret stores, secrets in this document can reference values from these stores if they are required
references:
  reference1:
    type: azure-key-vault
    parameters:
      name: <a string>
      subscription: <a guid>


# encryption keys that are needed in the vault, these keys will be created if they don't exist.
keys:
  encryption-key-one:
    type: RSA
    size: 2048

# each entry below is a separate secret that will be maintained in the secret store
# the type identifies what kind of secret it is, and the parameters drive the creation of the secret
secrets:
  secret1:
    type: pizza-recipe
    parameters:
      # This is a 'SecretReference' parameter. Some secrets require other secrets to work.
      # These parameters can be specified as a raw string, or with explicit name and location
      # If a raw string is used, the referenced secret is assumed to be in the same vault as the current secret
      # location can be any of the entries in the 'references' collection
      doughToken:
        name: dough-provider-token
        location: reference1
      # other parameters are just strings, and their interpretation depends on the type of the secret
      cheese: cheddar
      sauce: marinara
      topping: pepperoni
```

## Secret Types
Each secret in the manifest has a specified type, and these types determine what parameters are required, and what ends up in the secret store. Some secret types require human interaction to generate and/or rotate. Some secret types also produce multiple values. These values are all stored with additional suffixes in the secret store. Each type is documented below.

### Azure Storage

#### Connection String
```yaml
type: azure-storage-connection-string
parameters:
  subscription: azure subscription id
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
  subscription: azure subscription id
  resourceGroup: azure resource group
  namespace: event hub namespace
  name: event hub name
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
  subscription: azure subscription id
  resourceGroup: azure resource group
  namespace: service bus namespace
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
  gitHubBotAccountSecret: secret reference to the github account this token is for
  gitHubBotAccountName: username of the github account
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
