function Import-KeyVaultCertificate {
    param(
        [string]$KeyVaultName,
        [string]$CertificateName,
        [string]$X509StoreName
    )
    
    Write-Verbose "Reading certificate $CertificateName from key vault $KeyVaultName"

    $bundle = az keyvault secret show -n $CertificateName --vault-name $KeyVaultName | ConvertFrom-Json
    $pfxBytes = [Convert]::FromBase64String($bundle.value);

    $store = New-Object System.Security.Cryptography.X509Certificates.X509Store -ArgumentList $X509StoreName,CurrentUser
    try {
      $store.Open("ReadWrite");
      $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2 -ArgumentList @([byte[]]$pfxBytes,"",[System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]"Exportable, PersistKeySet, UserKeySet")
      $store.Add($cert)
      return $cert
    }
    catch {
      throw $_
    }
    finally {
      $store.Close();
    }
}

function Remove-Certificate {
    param(
        [System.Security.Cryptography.X509Certificates.X509Certificate2]$cert,
        [string]$X509StoreName
    )

    Write-Verbose "Removing certificate $($cert.SubjectName.Name) from local store"

    $store = New-Object System.Security.Cryptography.X509Certificates.X509Store -ArgumentList $X509StoreName,CurrentUser
    try {
      $store.Open("ReadWrite");
      $store.Remove($cert);
    }
    catch {
      throw $_
    }
    finally {
      $store.Close();
    }
}

function Read-XmlElementAsHashtable
{
    Param (
        [System.Xml.XmlElement]
        $Element
    )

    $hashtable = @{}
    if ($Element.Attributes)
    {
        $Element.Attributes | 
            ForEach-Object {
                $boolVal = $null
                if ([bool]::TryParse($_.Value, [ref]$boolVal)) {
                    $hashtable[$_.Name] = $boolVal
                }
                else {
                    $hashtable[$_.Name] = $_.Value
                }
            }
    }

    return $hashtable
}

function Read-PublishProfile
{
    Param (
        [ValidateScript({Test-Path $_ -PathType Leaf})]
        [String]
        $PublishProfileFile
    )

    $publishProfileXml = [Xml] (Get-Content $PublishProfileFile)
    $publishProfile = @{}

    $publishProfile.ClusterConnectionParameters = Read-XmlElementAsHashtable $publishProfileXml.PublishProfile.Item("ClusterConnectionParameters")
    $publishProfile.UpgradeDeployment = Read-XmlElementAsHashtable $publishProfileXml.PublishProfile.Item("UpgradeDeployment")
    $publishProfile.CopyPackageParameters = Read-XmlElementAsHashtable $publishProfileXml.PublishProfile.Item("CopyPackageParameters")

    if ($publishProfileXml.PublishProfile["UpgradeDeployment"])
    {
        $publishProfile.UpgradeDeployment.Parameters = Read-XmlElementAsHashtable $publishProfileXml.PublishProfile.Item("UpgradeDeployment").Item("Parameters")
        if ($publishProfile.UpgradeDeployment["Mode"])
        {
            $publishProfile.UpgradeDeployment.Parameters[$publishProfile.UpgradeDeployment["Mode"]] = $true
        }
    }

    $publishProfileFolder = (Split-Path $PublishProfileFile)
    $publishProfile.ApplicationParameterFile = (Join-Path -Path $PublishProfileFolder -ChildPath $publishProfileXml.PublishProfile.ApplicationParameterFile.Path)

    return $publishProfile
}
