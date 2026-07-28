<#
.SYNOPSIS
    Memasang CircuitSync ke Revit 2025.

.DESCRIPTION
    Menyalin CircuitSync.addin dan folder CircuitSync\ ke folder Addins Revit.
    Default memasang untuk satu user saja (tidak butuh hak admin). Pakai
    -AllUsers untuk memasang ke seluruh mesin — itu butuh PowerShell as admin.

.EXAMPLE
    .\Install.ps1
    .\Install.ps1 -AllUsers
    .\Install.ps1 -Uninstall
#>

[CmdletBinding()]
param(
    [int]$RevitVersion = 2025,
    [switch]$AllUsers,
    [switch]$Uninstall
)

$ErrorActionPreference = 'Stop'

$root = if ($AllUsers) {
    Join-Path $env:ProgramData "Autodesk\Revit\Addins\$RevitVersion"
} else {
    Join-Path $env:APPDATA "Autodesk\Revit\Addins\$RevitVersion"
}

$manifest = Join-Path $root 'CircuitSync.addin'
$payload  = Join-Path $root 'CircuitSync'

if ($Uninstall) {
    if (Test-Path $manifest) { Remove-Item $manifest -Force }
    if (Test-Path $payload)  { Remove-Item $payload -Recurse -Force }
    Write-Host "CircuitSync dilepas dari Revit $RevitVersion."
    return
}

$source = $PSScriptRoot
$sourceManifest = Join-Path $source 'CircuitSync.addin'
$sourcePayload  = Join-Path $source 'CircuitSync'

if (-not (Test-Path $sourceManifest) -or -not (Test-Path $sourcePayload)) {
    throw "Jalankan skrip ini dari dalam folder hasil ekstrak ZIP; CircuitSync.addin dan folder CircuitSync harus ada di sebelahnya."
}

# Revit memegang DLL selama aplikasi terbuka, jadi menyalin ke atasnya akan gagal
# dengan pesan yang membingungkan.
if (Get-Process -Name 'Revit' -ErrorAction SilentlyContinue) {
    throw "Revit sedang berjalan. Tutup Revit dulu, lalu jalankan lagi."
}

New-Item -ItemType Directory -Path $root -Force | Out-Null
if (Test-Path $payload) { Remove-Item $payload -Recurse -Force }

Copy-Item $sourceManifest $manifest -Force
Copy-Item $sourcePayload  $payload  -Recurse -Force

Write-Host "CircuitSync dipasang ke $root"
Write-Host "Buka Revit $RevitVersion, lalu cari tab Electrical > CircuitSync."
