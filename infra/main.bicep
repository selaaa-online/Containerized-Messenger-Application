// ============================================================================
// Chat application — Azure Container Apps deployment
//
// Topology:
//   - chat-web    : Blazor Server, EXTERNAL HTTPS ingress (sole public entry).
//   - chat-server : raw TCP server, INTERNAL ingress only, single replica.
//   - PostgreSQL  : Azure Database for PostgreSQL Flexible Server.
//   - Logs        : chat.log persisted to an Azure Files share; container
//                   stdout is shipped to Log Analytics automatically.
//
// Deploy in two passes (see deploy.ps1): pass 1 provisions everything with a
// public placeholder image, pass 2 supplies the images built into ACR.
// ============================================================================

@description('Location for all resources.')
param location string = resourceGroup().location

@description('Base name used to derive resource names.')
param baseName string = 'chatapp'

@description('PostgreSQL administrator login.')
param postgresAdminUser string = 'chatadmin'

@description('PostgreSQL administrator password.')
@secure()
param postgresAdminPassword string

@description('Container image for the chat server. Defaults to a placeholder for the first pass.')
param serverImage string = 'mcr.microsoft.com/k8se/quickstart:latest'

@description('Container image for the Blazor web client. Defaults to a placeholder for the first pass.')
param webImage string = 'mcr.microsoft.com/k8se/quickstart:latest'

@description('TCP port the chat server listens on.')
param serverPort int = 9000

// ---- Derived names ----
var uniqueSuffix = uniqueString(resourceGroup().id)
var acrName = toLower('${baseName}acr${uniqueSuffix}')
var lawName = '${baseName}-law'
var envName = '${baseName}-env'
var storageName = toLower('${baseName}st${uniqueSuffix}')
var logShareName = 'chatlogs'
var pgName = toLower('${baseName}-pg-${uniqueSuffix}')
var dbName = 'chatdb'
var uamiName = '${baseName}-pull-identity'
var acrPullRoleId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')

// ---- User-assigned identity used by both apps to pull from ACR ----
resource uami 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: uamiName
  location: location
}

// ---- Azure Container Registry ----
resource acr 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: acrName
  location: location
  sku: {
    name: 'Basic'
  }
  properties: {
    adminUserEnabled: false
  }
}

resource acrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(acr.id, uami.id, acrPullRoleId)
  scope: acr
  properties: {
    principalId: uami.properties.principalId
    roleDefinitionId: acrPullRoleId
    principalType: 'ServicePrincipal'
  }
}

// ---- Log Analytics (backs Container Apps log streaming) ----
resource law 'Microsoft.OperationalInsights/workspaces@2022-10-01' = {
  name: lawName
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
  }
}

// ---- Storage account + file share for the append-only chat.log ----
resource storage 'Microsoft.Storage/storageAccounts@2023-01-01' = {
  name: storageName
  location: location
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
  }
}

resource fileService 'Microsoft.Storage/storageAccounts/fileServices@2023-01-01' = {
  parent: storage
  name: 'default'
}

resource logShare 'Microsoft.Storage/storageAccounts/fileServices/shares@2023-01-01' = {
  parent: fileService
  name: logShareName
  properties: {
    shareQuota: 5
  }
}

// ---- PostgreSQL Flexible Server ----
resource pg 'Microsoft.DBforPostgreSQL/flexibleServers@2023-06-01-preview' = {
  name: pgName
  location: location
  sku: {
    name: 'Standard_B1ms'
    tier: 'Burstable'
  }
  properties: {
    version: '16'
    administratorLogin: postgresAdminUser
    administratorLoginPassword: postgresAdminPassword
    storage: {
      storageSizeGB: 32
    }
    backup: {
      backupRetentionDays: 7
      geoRedundantBackup: 'Disabled'
    }
    highAvailability: {
      mode: 'Disabled'
    }
    authConfig: {
      passwordAuth: 'Enabled'
      activeDirectoryAuth: 'Disabled'
    }
  }
}

// Allow other Azure services (the Container Apps environment) to reach the DB.
resource pgFirewall 'Microsoft.DBforPostgreSQL/flexibleServers/firewallRules@2023-06-01-preview' = {
  parent: pg
  name: 'AllowAllAzureServices'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource pgDb 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2023-06-01-preview' = {
  parent: pg
  name: dbName
  properties: {
    charset: 'UTF8'
    collation: 'en_US.utf8'
  }
}

// ---- Container Apps managed environment ----
resource env 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: envName
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: law.properties.customerId
        sharedKey: law.listKeys().primarySharedKey
      }
    }
  }
}

// Environment storage backed by Azure Files, mounted by the server for chat.log.
resource envStorage 'Microsoft.App/managedEnvironments/storages@2024-03-01' = {
  parent: env
  name: logShareName
  properties: {
    azureFile: {
      accountName: storage.name
      accountKey: storage.listKeys().keys[0].value
      shareName: logShareName
      accessMode: 'ReadWrite'
    }
  }
}

// ---- Chat app: server + web co-located in one replica ----
// The two containers share a network namespace, so the web talks to the server
// over localhost:9000. The server therefore needs no ingress of its own and stays
// fully private; only the web's HTTPS ingress is publicly reachable. A single
// replica keeps the server's in-memory client registry authoritative.
resource chatApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: baseName
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${uami.id}': {}
    }
  }
  properties: {
    managedEnvironmentId: env.id
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        transport: 'auto'
        targetPort: 8080
        allowInsecure: false
      }
      registries: [
        {
          server: acr.properties.loginServer
          identity: uami.id
        }
      ]
      secrets: [
        {
          name: 'postgres-password'
          value: postgresAdminPassword
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'chat-server'
          image: serverImage
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: [
            {
              name: 'SERVER_PORT'
              value: string(serverPort)
            }
            {
              name: 'LOG_FILE_PATH'
              value: '/app/logs/chat.log'
            }
            {
              name: 'POSTGRES_HOST'
              value: pg.properties.fullyQualifiedDomainName
            }
            {
              name: 'POSTGRES_PORT'
              value: '5432'
            }
            {
              name: 'POSTGRES_DB'
              value: dbName
            }
            {
              name: 'POSTGRES_USER'
              value: postgresAdminUser
            }
            {
              name: 'POSTGRES_PASSWORD'
              secretRef: 'postgres-password'
            }
            {
              name: 'POSTGRES_SSL_MODE'
              value: 'Require'
            }
          ]
          volumeMounts: [
            {
              volumeName: 'logs'
              mountPath: '/app/logs'
            }
          ]
        }
        {
          name: 'chat-web'
          image: webImage
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: [
            {
              name: 'SERVER_HOST'
              value: 'localhost'
            }
            {
              name: 'SERVER_PORT'
              value: string(serverPort)
            }
          ]
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 1
      }
      volumes: [
        {
          name: 'logs'
          storageType: 'AzureFile'
          storageName: envStorage.name
        }
      ]
    }
  }
  dependsOn: [
    acrPull
    pgFirewall
    pgDb
  ]
}

// ---- Outputs ----
output acrName string = acr.name
output acrLoginServer string = acr.properties.loginServer
output webUrl string = 'https://${chatApp.properties.configuration.ingress.fqdn}'
output postgresFqdn string = pg.properties.fullyQualifiedDomainName
