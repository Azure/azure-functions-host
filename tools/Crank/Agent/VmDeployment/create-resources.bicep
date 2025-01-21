@description('The location of the resources')
param location string = resourceGroup().location

@description('The names of the virtual machines')
param vmNames array

@description('The admin username for the virtual machines')
param adminUsername string

@description('The admin password for the virtual machines')
@secure()
param adminPassword string

@description('Base64 encoded JSON parameters')
param parametersJsonBase64 string

@description('Windows local admin username')
param windowsLocalAdminUserName string

@description('Base64 encoded Windows local admin password')
param windowsLocalAdminPasswordBase64 string

@description('The operating system type')
param osType string = 'Windows'

@description('The name of the virtual network')
param virtualNetworkName string = 'shared-vnet'

@description('The name of the subnet')
param subnetName string = 'default'

@description('The name of the network security group')
param networkSecurityGroupName string = 'shared-nsg'

@description('The name of the private DNS zone')
param privateDnsZoneName string = 'shared.private'

@description('The size of the virtual machines')
param vmSize string

@description('The type of the OS disk')
param osDiskType string

resource virtualNetwork 'Microsoft.Network/virtualNetworks@2021-02-01' = {
  name: virtualNetworkName
  location: location
  properties: {
    addressSpace: {
      addressPrefixes: [
        '10.0.0.0/16'
      ]
    }
    subnets: [
      {
        name: subnetName
        properties: {
          addressPrefix: '10.0.0.0/24'
        }
      }
    ]
  }
}

resource networkSecurityGroup 'Microsoft.Network/networkSecurityGroups@2021-02-01' = {
  name: networkSecurityGroupName
  location: location
  properties: {
    securityRules: [
      {
        name: 'AllowInternetOutbound'
        properties: {
          priority: 100
          protocol: '*'
          access: 'Allow'
          direction: 'Outbound'
          sourceAddressPrefix: '*'
          sourcePortRange: '*'
          destinationAddressPrefix: 'Internet'
          destinationPortRange: '*'
        }
      }
      {
        name: 'SSH'
        properties: {
          priority: 1000
          protocol: 'TCP'
          access: 'Allow'
          direction: 'Inbound'
          sourceAddressPrefix: '*'
          sourcePortRange: '*'
          destinationAddressPrefix: '*'
          destinationPortRange: '22'
        }
      }
      {
        name: 'RDP'
        properties: {
          priority: 1001
          protocol: 'TCP'
          access: 'Allow'
          direction: 'Inbound'
          sourceAddressPrefix: '*'
          sourcePortRange: '*'
          destinationAddressPrefix: '*'
          destinationPortRange: '3389'
        }
      }
      {
        name: 'DotNet-Crank'
        properties: {
          priority: 1011
          protocol: '*'
          access: 'Allow'
          direction: 'Inbound'
          sourceAddressPrefix: '*'
          sourcePortRange: '*'
          destinationAddressPrefix: '*'
          destinationPortRange: '5010'
        }
      }
      {
        name: 'Benchmark-App'
        properties: {
          priority: 1012
          protocol: '*'
          access: 'Allow'
          direction: 'Inbound'
          sourceAddressPrefix: '*'
          sourcePortRange: '*'
          destinationAddressPrefix: '*'
          destinationPortRange: '5000'
        }
      }
    ]
  }
}

resource publicIPAddresses 'Microsoft.Network/publicIPAddresses@2021-02-01' = [
  for (vmName, index) in vmNames: {
    name: '${vmName}-pip'
    location: location
    properties: {
      publicIPAllocationMethod: 'Dynamic'
      dnsSettings: {
        domainNameLabel: vmName
      }
    }
  }
]

resource networkInterfaces 'Microsoft.Network/networkInterfaces@2021-02-01' = [
  for (vmName, index) in vmNames: {
    name: '${vmName}-nic'
    location: location
    properties: {
      ipConfigurations: [
        {
          name: 'ipconfig1'
          properties: {
            subnet: {
              id: virtualNetwork.properties.subnets[0].id
            }
            privateIPAllocationMethod: 'Dynamic'
            publicIPAddress: {
              id: publicIPAddresses[index].id
            }
          }
        }
      ]
      networkSecurityGroup: {
        id: networkSecurityGroup.id
      }
    }
  }
]

resource virtualMachines 'Microsoft.Compute/virtualMachines@2021-07-01' = [
  for (vmName, index) in vmNames: {
    name: vmName
    location: location
    properties: {
      hardwareProfile: {
        vmSize: vmSize
      }
      osProfile: {
        computerName: vmName
        adminUsername: adminUsername
        adminPassword: adminPassword
      }
      storageProfile: {
        imageReference: {
          publisher: 'MicrosoftWindowsServer'
          offer: 'WindowsServer'
          sku: '2022-Datacenter'
          version: 'latest'
        }
        osDisk: {
          createOption: 'FromImage'
          managedDisk: {
            storageAccountType: osDiskType
          }
        }
      }
      networkProfile: {
        networkInterfaces: [
          {
            id: networkInterfaces[index].id
          }
        ]
      }
    }
  }
]

resource customScriptExtensions 'Microsoft.Compute/virtualMachines/extensions@2021-03-01' = [
  for (vmName, index) in vmNames: if (osType == 'Windows') {
    name: '${vmName}-bootstrap'
    parent: virtualMachines[index]
    location: location
    properties: {
      publisher: 'Microsoft.Compute'
      type: 'CustomScriptExtension'
      typeHandlerVersion: '1.10'
      autoUpgradeMinorVersion: true
      settings: {
        fileUris: [
          'https://raw.githubusercontent.com/Azure/azure-functions-host/refs/heads/shkr/crankreturns/tools/Crank/Agent/Windows/bootstrap.ps1'
        ]
        commandToExecute: 'powershell.exe -ExecutionPolicy Unrestricted -NoProfile -NonInteractive -File .\\bootstrap.ps1 -ParametersJsonBase64 ${parametersJsonBase64} -WindowsLocalAdminUserName ${windowsLocalAdminUserName} -WindowsLocalAdminPasswordBase64 ${windowsLocalAdminPasswordBase64}'
      }
    }
  }
]

resource privateDnsZone 'Microsoft.Network/privateDnsZones@2020-06-01' = {
  name: privateDnsZoneName
  location: 'global'
}

resource virtualNetworkLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2020-06-01' = {
  name: '${privateDnsZoneName}-vnet-link'
  parent: privateDnsZone
  location: 'global'
  properties: {
    virtualNetwork: {
      id: virtualNetwork.id
    }
    registrationEnabled: true
  }
}

resource aRecords 'Microsoft.Network/privateDnsZones/A@2020-06-01' = [
  for (vmName, index) in vmNames: {
    name: '${vmName}.${privateDnsZoneName}'
    parent: privateDnsZone
    properties: {
      ttl: 3600
      aRecords: [
        {
          ipv4Address: networkInterfaces[index].properties.ipConfigurations[0].properties.privateIPAddress
        }
      ]
    }
  }
]

output adminUsername string = adminUsername
output hostnames array = [for (vmName, index) in vmNames: publicIPAddresses[index].properties.dnsSettings.fqdn]
output sshCommands array = [
  for (vmName, index) in vmNames: 'ssh ${adminUsername}@${publicIPAddresses[index].properties.dnsSettings.fqdn}'
]
