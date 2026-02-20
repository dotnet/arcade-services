// azure system role for setting up acr pull access
var acrPullRole = subscriptionResourceId(
  'Microsoft.Authorization/roleDefinitions',
  '7f951dda-4ed3-4680-a7ca-43fe172d538d'
)

// azure system role for granting push access
var acrPushRole = subscriptionResourceId(
  'Microsoft.Authorization/roleDefinitions',
  '8311e382-0749-4cb8-b61a-304f252e45ec'
)

// azure system role for setting secret access
var kvSecretUserRole = subscriptionResourceId(
  'Microsoft.Authorization/roleDefinitions',
  '4633458b-17de-408a-b874-0445c86b69e6'
)

// azure system role for setting storage queue access
var storageQueueContributorRole = subscriptionResourceId(
  'Microsoft.Authorization/roleDefinitions',
  '974c5e8b-45b9-4653-ba55-5f855dd0fb88'
)

// azure system role for setting contributor access
var contributorRole = subscriptionResourceId(
  'Microsoft.Authorization/roleDefinitions',
  'b24988ac-6180-42a0-ab88-20f7382dd24c'
)

// storage account blob contributor
var blobContributorRole = subscriptionResourceId(
  'Microsoft.Authorization/roleDefinitions',
  'ba92f5b4-2d11-453d-a403-e96b0029c9fe'
)

// Key Vault Crypto User role
var kvCryptoUserRole = subscriptionResourceId(
  'Microsoft.Authorization/roleDefinitions',
  '12338af0-0e69-4776-bea7-57ae8d297424'
)

// Reader role
var readerRole = subscriptionResourceId(
  'Microsoft.Authorization/roleDefinitions',
  'acdd72a7-3385-48ef-bd42-f606fba81ae7'
)

// Container Apps ManagedEnvironments Contributor Role
var containerAppsManagedEnvironmentsContributor = subscriptionResourceId(
  'Microsoft.Authorization/roleDefinitions',
  '57cc5028-e6a7-4284-868d-0611c5923f8d'
)

output acrPullRole string = acrPullRole
output acrPushRole string = acrPushRole
output kvSecretUserRole string = kvSecretUserRole
output storageQueueContributorRole string = storageQueueContributorRole
output contributorRole string = contributorRole
output blobContributorRole string = blobContributorRole
output kvCryptoUserRole string = kvCryptoUserRole
output readerRole string = readerRole
output containerAppsManagedEnvironmentsContributor string = containerAppsManagedEnvironmentsContributor
