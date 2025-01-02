using 'application-gateway.bicep'

param appGwName = 'product-construction-service-agw-int'
param location = 'westus2'
param kvName = 'ProductConstructionInt'
param appGwIdentityName = 'AppGwIdentityInt'
param certificateName = 'maestro-int-ag'
// Certificate Secret identifier, without the last part (the version)
param certificateSecretIdShort = 'https://productconstructionint.vault.azure.net/secrets/maestro-int-ag'
param virtualNetworkName = 'product-construction-service-vnet-int'
param appGwVirtualNetworkSubnetName = 'AppGateway'
param nsgName = 'product-construction-service-nsg-int'
param publicIpAddressName = 'product-construction-service-public-ip-int'
param frontendIpName = 'frontendIp'
param httpPortName = 'httpPort'
param httpsPortName = 'httpsPort'
param pcsPool = 'pcs'
param containerAppName = 'product-construction-int'
param backendHttpSettingName = 'backendHttpSetting'
param backendHttpsSettingName = 'backendHttpsSetting'
param pcs80listener = 'pcs-listener-80'
param pcs443listener = 'pcs-listener-443'
param pcsRedirection  = 'pcs-redirection'
param pcs80rule = 'pcs-rule-80'
param pcs443rule = 'pcs-rule-443'
param containerEnvironmentName = 'product-construction-service-env-int'
param hostName = 'maestro.int-dot.net'
