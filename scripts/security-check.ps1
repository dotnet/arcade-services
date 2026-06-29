$ErrorActionPreference = 'Stop'
# Block destructive commands — customize this blocklist for your repo
$blockedPatterns = @('rm -rf /', 'DROP DATABASE', 'format C:', 'mkfs', 'git push --force')
$commandText = $args -join ' '
foreach ($pattern in $blockedPatterns) {
    if ($commandText -match [regex]::Escape($pattern)) {
        Write-Error "Blocked: destructive pattern detected ($pattern)"
        exit 1
    }
}
