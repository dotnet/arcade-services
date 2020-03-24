[CmdletBinding()]
param(
    [Parameter(Mandatory=$True)] [string]$ApplicationName,
    [Parameter(Mandatory=$True)] [string]$ApplicationManifestPath,
    [Parameter(Mandatory=$True)] [string]$ServicesSourceFolder,
    [int]$TimeoutInSecs = 300
)

function Get-ServiceTypes() {
    $serviceTypes = @()
    [xml]$appManifest = Get-Content $ApplicationManifestPath 
    $manifestsNames = $appManifest.ApplicationManifest.ServiceManifestImport.ServiceManifestRef.ServiceManifestName | Select-Object $_

    foreach ($manifestName in $manifestsNames) {
        $serviceName = $manifestName -replace "Pkg" 
        $manifestPath = "${ServicesSourceFolder}${serviceName}\PackageRoot\ServiceManifest.xml"

        [xml]$serviceManifest = Get-Content $manifestPath
        $serviceTypes += $serviceManifest.ServiceManifest.ServiceTypes.StatefulServiceType.ServiceTypeName | Select-Object $_
        $serviceTypes += $serviceManifest.ServiceManifest.ServiceTypes.StatelessServiceType.ServiceTypeName | Select-Object $_
    }

    Write-Host "Service types found: "
    $serviceTypes | ForEach-Object { Write-Host "`t" $_ }
    Write-Host
    
    return $serviceTypes
}

try {
    $serviceTypes = Get-ServiceTypes

    $runningServices = Get-ServiceFabricService -ApplicationName $ApplicationName

    Write-Host "Currently running services: "
    $runningServices | ForEach-Object { Write-Host "`t" $_.ServiceName ":" $_.ServiceTypeName }
    Write-Host

    $runningServices | ForEach-Object {
        $serviceName = $_.ServiceName
        $serviceTypeName = $_.ServiceTypeName
        $shouldBeRemoved = ! ($serviceTypeName -in $serviceTypes)

        Write-Host "Should '$serviceName : $serviceTypeName' service be removed? $shouldBeRemoved"

        if ($shouldBeRemoved) {
            Write-Host -NoNewline "`t Removing '$serviceName' service ... "  -ForegroundColor White -BackgroundColor Red
            Remove-ServiceFabricService -ServiceName $serviceName -Force -ForceRemove -TimeOutSec $TimeoutInSecs | Out-Null
            Write-Host "done." -ForegroundColor White -BackgroundColor Red
        }
    }
}
catch {
	Write-Error "Problems while removing service fabric services."
	Write-Error $_
}
