##############################
#.SYNOPSIS
#  Wrapper for git.exe that handles errors and warnings the powershell way
#
##############################
function git
{
    [cmdletbinding()]
    param(
        [Parameter(ValueFromRemainingArguments=$true)] $params
    )
    $ErrorActionPreference = "Continue"
    $allOutput = & git.exe $params 2>&1

    $allOutput | ForEach-Object {
        if ($_ -is [System.Management.Automation.ErrorRecord])
        {
            $message = $_.Exception.Message
            if ($message -match '^warning: ')
            {
                Write-Warning $message
            }
            else
            {
                Write-Error $message
            }
        }
        else
        {
            Write-Output $_
        }
    }
}

Export-ModuleMember -Function @(
  'git'
)
