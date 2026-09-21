# Builds dist\INIMaster.exe (single file, self-contained, win-x64) and a zip
# with the docs and the SDK beside it.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$dist = Join-Path $root 'dist'
$out = Join-Path $dist 'app'

dotnet test (Join-Path $root 'tests\IniMaster.Tests') -nologo
if ($LASTEXITCODE) { throw 'tests failed' }

Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish (Join-Path $root 'src\IniMaster\IniMaster.csproj') -nologo -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $out
if ($LASTEXITCODE) { throw 'publish failed' }

[xml]$proj = Get-Content (Join-Path $root 'src\IniMaster\IniMaster.csproj')
$version = ($proj.Project.PropertyGroup | Where-Object Version | Select-Object -First 1).Version

$stage = Join-Path $dist 'stage'
Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force "$stage\docs", "$stage\sdk\example", "$stage\inimeta" | Out-Null
Copy-Item "$out\INIMaster.exe" $stage
Copy-Item "$out\inimeta\*" "$stage\inimeta" -ErrorAction SilentlyContinue
Copy-Item (Join-Path $root 'README.md') $stage
Copy-Item (Join-Path $root 'docs\METADATA.md') "$stage\docs"
Copy-Item (Join-Path $root 'sdk\inimaster.h') "$stage\sdk"
Get-ChildItem (Join-Path $root 'sdk\example') -File | Copy-Item -Destination "$stage\sdk\example"

Copy-Item "$out\INIMaster.exe" $dist -Force
$zip = Join-Path $dist "INIMaster-$version.zip"
Remove-Item $zip -ErrorAction SilentlyContinue
Compress-Archive -Path "$stage\*" -DestinationPath $zip
Remove-Item $stage -Recurse -Force

$size = [math]::Round((Get-Item "$dist\INIMaster.exe").Length / 1MB, 1)
"dist\INIMaster.exe ($size MB) and $(Split-Path $zip -Leaf)"
