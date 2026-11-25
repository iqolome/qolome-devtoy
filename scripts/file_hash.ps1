# Hash helpers that do not depend on Get-FileHash.

function Get-FileDigestHex {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][System.Security.Cryptography.HashAlgorithm]$Algorithm
    )

    $stream = $null
    try {
        $stream = [System.IO.File]::Open(
            $Path,
            [System.IO.FileMode]::Open,
            [System.IO.FileAccess]::Read,
            [System.IO.FileShare]::ReadWrite)
        $hash = $Algorithm.ComputeHash($stream)
    } finally {
        if ($null -ne $stream) { $stream.Dispose() }
        $Algorithm.Dispose()
    }
    return ([System.BitConverter]::ToString($hash)).Replace("-", "").ToLowerInvariant()
}

function Get-FileSha256Hex {
    param([Parameter(Mandatory = $true)][string]$Path)
    return Get-FileDigestHex -Path $Path -Algorithm ([System.Security.Cryptography.SHA256]::Create())
}