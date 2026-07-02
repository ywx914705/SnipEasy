param(
    [Parameter(Mandatory = $true)]
    [string]$ExePath,

    [Parameter(Mandatory = $true)]
    [string]$IconPath
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $ExePath)) {
    throw "Executable was not found: $ExePath"
}

if (-not (Test-Path $IconPath)) {
    throw "Icon file was not found: $IconPath"
}

Add-Type @"
using System;
using System.Runtime.InteropServices;

public static class SnipEasyResourceUpdater
{
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr BeginUpdateResource(string pFileName, bool bDeleteExistingResources);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool UpdateResource(IntPtr hUpdate, IntPtr lpType, IntPtr lpName, ushort wLanguage, byte[] lpData, int cbData);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool EndUpdateResource(IntPtr hUpdate, bool fDiscard);
}
"@

function Read-UInt16([byte[]]$Data, [int]$Offset) {
    return [BitConverter]::ToUInt16($Data, $Offset)
}

function Read-UInt32([byte[]]$Data, [int]$Offset) {
    return [BitConverter]::ToUInt32($Data, $Offset)
}

$ico = [System.IO.File]::ReadAllBytes($IconPath)
if ((Read-UInt16 $ico 0) -ne 0 -or (Read-UInt16 $ico 2) -ne 1) {
    throw "The icon file is not a valid ICO file."
}

$count = Read-UInt16 $ico 4
if ($count -lt 1) {
    throw "The icon file does not contain any icon images."
}

$images = New-Object System.Collections.Generic.List[object]
for ($i = 0; $i -lt $count; $i++) {
    $entryOffset = 6 + ($i * 16)
    $bytesInRes = Read-UInt32 $ico ($entryOffset + 8)
    $imageOffset = Read-UInt32 $ico ($entryOffset + 12)

    $imageBytes = New-Object byte[] $bytesInRes
    [Array]::Copy($ico, $imageOffset, $imageBytes, 0, $bytesInRes)

    $images.Add([pscustomobject]@{
        Width      = $ico[$entryOffset]
        Height     = $ico[$entryOffset + 1]
        ColorCount = $ico[$entryOffset + 2]
        Reserved   = $ico[$entryOffset + 3]
        Planes     = Read-UInt16 $ico ($entryOffset + 4)
        BitCount   = Read-UInt16 $ico ($entryOffset + 6)
        BytesInRes = $bytesInRes
        Id         = $i + 1
        Data       = $imageBytes
    })
}

$groupStream = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter($groupStream)
$writer.Write([UInt16]0)
$writer.Write([UInt16]1)
$writer.Write([UInt16]$count)

foreach ($image in $images) {
    $writer.Write([byte]$image.Width)
    $writer.Write([byte]$image.Height)
    $writer.Write([byte]$image.ColorCount)
    $writer.Write([byte]$image.Reserved)
    $writer.Write([UInt16]$image.Planes)
    $writer.Write([UInt16]$image.BitCount)
    $writer.Write([UInt32]$image.BytesInRes)
    $writer.Write([UInt16]$image.Id)
}

$writer.Flush()
$groupBytes = $groupStream.ToArray()
$writer.Dispose()
$groupStream.Dispose()

$rtIcon = [IntPtr]3
$rtGroupIcon = [IntPtr]14
$groupIconId = [IntPtr]1
$languages = @(0, 1033, 2052)

$handle = [SnipEasyResourceUpdater]::BeginUpdateResource($ExePath, $false)
if ($handle -eq [IntPtr]::Zero) {
    throw "BeginUpdateResource failed: $([Runtime.InteropServices.Marshal]::GetLastWin32Error())"
}

$discard = $true
try {
    foreach ($language in $languages) {
        foreach ($image in $images) {
            $ok = [SnipEasyResourceUpdater]::UpdateResource(
                $handle,
                $rtIcon,
                [IntPtr]$image.Id,
                [UInt16]$language,
                $image.Data,
                $image.Data.Length
            )

            if (-not $ok) {
                throw "UpdateResource RT_ICON failed: $([Runtime.InteropServices.Marshal]::GetLastWin32Error())"
            }
        }

        $ok = [SnipEasyResourceUpdater]::UpdateResource(
            $handle,
            $rtGroupIcon,
            $groupIconId,
            [UInt16]$language,
            $groupBytes,
            $groupBytes.Length
        )

        if (-not $ok) {
            throw "UpdateResource RT_GROUP_ICON failed: $([Runtime.InteropServices.Marshal]::GetLastWin32Error())"
        }
    }

    $discard = $false
}
finally {
    $ok = [SnipEasyResourceUpdater]::EndUpdateResource($handle, $discard)
    if (-not $ok) {
        throw "EndUpdateResource failed: $([Runtime.InteropServices.Marshal]::GetLastWin32Error())"
    }
}

Get-Item -LiteralPath $ExePath
