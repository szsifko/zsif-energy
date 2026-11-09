# Builds a self-contained linux-x64 release and packs it with the deploy files.
# Result: artifacts/zsif-energy-linux-x64.tar.gz
$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $root "artifacts"
$staging = Join-Path $artifacts "zsif-energy-linux-x64"
$archive = Join-Path $artifacts "zsif-energy-linux-x64.tar.gz"

if (Test-Path $staging) {
    Remove-Item -Recurse -Force $staging
}
if (Test-Path $archive) {
    Remove-Item -Force $archive
}

dotnet publish (Join-Path $root "src/Zsif.Energy.Worker") `
    --configuration Release `
    --runtime linux-x64 `
    --self-contained true `
    --output (Join-Path $staging "app")
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed"
}

Copy-Item (Join-Path $root "deploy/*") $staging

tar -czf $archive -C $artifacts "zsif-energy-linux-x64"
if ($LASTEXITCODE -ne 0) {
    throw "tar failed"
}

Write-Host "Created $archive"
