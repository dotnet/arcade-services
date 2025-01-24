### Creates an issue, assigns it to the PCS and UB projects, sets the area/size fields and opens the issue in the browser.
### Parameters:
###   -t, --title <string> Title of the issue
###   -b, --body <string> Body of the issue
###   -a, --area <Pcs|Ri|Fr> Area of the issue
###   -s, --size <XS|S|M|L|XL|U> Size of the issue
### Example: .\new-issue.ps1 -t Title -b Body -a Pcs -s M

[CmdletBinding(PositionalBinding=$false)]
Param(
    [Alias('r')]
    [Parameter(Mandatory=$false)]
    [string]
    $Repo = 'dotnet/arcade-services',

    [Alias('t')]
    [Parameter(Mandatory=$true)]
    [string]
    $Title,

    [Alias('b')]
    [Parameter(Mandatory=$false)]
    [string]
    $Body,

    [Alias('a')]
    [Parameter(Mandatory=$true)]
    [ValidateSet('Pcs', 'Ri', 'Fr')]
    [string]
    $Area,

    [Alias('s')]
    [Parameter(Mandatory=$false)]
    [ValidateSet('XS', 'S', 'M', 'L', 'XL', 'U')]
    [string]
    $Size
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true

$pcsProject = 276;
$ubProject = 310;

switch ($Area) {
    'Pcs' { $areaName = 'Product Construction' }
    'Ri' { $areaName = 'Release Infrastructure' }
    'Fr' { $areaName = 'First Responder / Ops / Debt' }
}

function Set-ProjectProperty($projectId, $issue, $property, $value) {
    $project = gh project view --owner dotnet --format json $projectId | ConvertFrom-Json
    $projectItem = gh project item-add $projectId --owner dotnet --url $issue.url --format json | ConvertFrom-Json
    $field = gh project field-list $projectId --format json --owner dotnet --jq ".fields[] | select(.name == `"$property`")" | ConvertFrom-Json
    $option = $field.options | Where-Object { $_.name -eq $value }
    gh project item-edit --id $projectItem.id --project-id $project.id --field-id $field.id --single-select-option-id $option.id
}

$issueUrl = gh issue create --title "$Title" --body "$Body" --repo $Repo
Write-Host "Created issue $issueUrl"
$issue = gh issue view $issueUrl --json id,url | ConvertFrom-Json

Set-ProjectProperty $pcsProject $issue 'Area' $areaName

if ($Area -eq 'Ri') {
    $areaName = 'Release Infra'
}

if ($Area -ne 'Fr') {
    Set-ProjectProperty $ubProject $issue 'Area' $areaName
    if ($Size) {
        Set-ProjectProperty $ubProject $issue 'Size' $Size
    }
}

start $issue.url
