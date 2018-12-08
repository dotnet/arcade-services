[CmdletBinding()]
param(
	[Parameter(Mandatory=$True)]
	[string]$ConnectionString,
	[Parameter(Mandatory=$True)]
	[string]$RuleName,
	[switch]$Add,
	[switch]$Remove
)

function Get-FirewalledIp() {
	try {
		$ipConn = new-object System.Data.SqlClient.SqlConnection($ConnectionString)
		$ipConn.Open()
	} catch {
		if ($_ -match "Client with IP address '([0-9.]+)'") {
			$ip = $matches[1]
			if ($ipConn.DataSource -match ".*:(.*),\d*") {
				$server = $matches[1]
				return $server, $ip
			}
		}
	} finally {
		$ipConn.Dispose()
	}
}

function Get-ServerName() {
	try {
		$ipConn = new-object System.Data.SqlClient.SqlConnection($ConnectionString)
		if ($ipConn.DataSource -match ".*:(.*),\d*") {
			$server = $matches[1]
			return $server
		}
	} finally {
		$ipConn.Dispose()
	}
}

if ($Add) {
	$server, $ip = (Get-FirewalledIp)
	if (-not $ip) {
		Write-Host "Firewall not detected. No action taken."
		exit 0
	}
	
	if (-not $server) {
		throw "Unable to parse server from DataSource."
	}
	
	Write-Host "IP Address $ip requires firewall rule..."
	Write-Host "Creating rule named '$RuleName'..."
	Write-Host "Searching for server '$server'..."
	$resource = Get-AzureRmSqlServer | where FullyQualifiedDomainName -eq $server
	Write-Host "Found server '$($resource.ServerName)' in resource group '$($resource.ResourceGroupName)'."
	New-AzureRmSqlServerFirewallRule -ServerName $resource.ServerName -ResourceGroupName $resource.ResourceGroupName -FirewallRuleName $RuleName -StartIpAddress $ip -EndIpAddress $ip | Out-Null
	Write-Host "Done!"
	exit 0
}

if ($Remove) {
	$server = Get-ServerName
	if (-not $server) {
		throw "Unable to parse server from DataSource."
	}
	
	Write-Host "Removing rule '$RuleName'..."
	Write-Host "Searching for server '$server'..."
	$resource = Get-AzureRmSqlServer | where FullyQualifiedDomainName -eq $server
	Write-Host "Found server '$($resource.ServerName)' in resource group '$($resource.ResourceGroupName)'."
	$existing = Get-AzureRmSqlServerFirewallRule -ServerName $resource.ServerName -ResourceGroupName $resource.ResourceGroupName | where FirewallRuleName -eq $RuleName
	if (-not $existing) {
		Write-Host "No rule detected. No action taken."
		exit 0
	}
	$existing | Remove-AzureRmSqlServerFirewallRule | Out-Null
	Write-Host "Done!"
	exit 0
}

throw "One of -Add or -Remove is required"
