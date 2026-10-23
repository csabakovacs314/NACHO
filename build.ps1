# Builds a NACHO release into a NEW folder: Downloads\NACHO\NACHO-<version>
# Steps: icon, unit tests, two exe flavours, self-test of both, source zip, package zip.
param([string]$Version = "0.1.1")
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$release = Join-Path $env:USERPROFILE "Downloads\NACHO\NACHO-$Version"
if (Test-Path $release) { throw "Release folder already exists: $release. A new version needs a new folder." }

Write-Host "1/6 Icon"
& (Join-Path $root "tools\make-icon.ps1") -Out (Join-Path $root "src\Nacho\nacho.ico")

Write-Host "2/6 Unit tests"
dotnet test (Join-Path $root "tests\Nacho.Tests\Nacho.Tests.csproj") -c Release -nologo
if ($LASTEXITCODE -ne 0) { throw "Unit tests failed." }

Write-Host "3/6 Publish portable and small"
$out = Join-Path $root "out"
dotnet publish (Join-Path $root "src\Nacho\Nacho.csproj") -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -o (Join-Path $out "portable") -nologo
if ($LASTEXITCODE -ne 0) { throw "Portable publish failed." }
dotnet publish (Join-Path $root "src\Nacho\Nacho.csproj") -c Release -r win-x64 --self-contained false `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -o (Join-Path $out "small") -nologo
if ($LASTEXITCODE -ne 0) { throw "Small publish failed." }

Write-Host "4/6 Release folder"
New-Item -ItemType Directory -Path (Join-Path $release "Small") -Force | Out-Null
Copy-Item (Join-Path $out "portable\NACHO.exe") $release
Copy-Item (Join-Path $out "small\NACHO.exe") (Join-Path $release "Small")
Copy-Item (Join-Path $root "README.txt"), (Join-Path $root "CHANGELOG.md") $release

Write-Host "5/6 Self-test both exes from the release folder"
foreach ($exe in @((Join-Path $release "NACHO.exe"), (Join-Path $release "Small\NACHO.exe"))) {
    $dir = Join-Path $out ("selftest-" + (Split-Path (Split-Path $exe -Parent) -Leaf))
    $p = Start-Process -FilePath $exe -ArgumentList "--selftest", "`"$dir`"" -Wait -PassThru
    if ($p.ExitCode -ne 0) { throw "Self-test failed for $exe (exit $($p.ExitCode)). See $dir\selftest-report.txt" }
}

Write-Host "6/6 Zips"
$stage = Join-Path $out "source-stage"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Path $stage | Out-Null
foreach ($item in "src", "tests", "tools", "Directory.Build.props", "build.ps1", "README.txt", "CHANGELOG.md") {
    Copy-Item (Join-Path $root $item) $stage -Recurse
}
Get-ChildItem $stage -Recurse -Directory | Where-Object { $_.Name -in "bin", "obj" } | Remove-Item -Recurse -Force
Compress-Archive -Path (Join-Path $stage "*") -DestinationPath (Join-Path $release "NACHO-$Version-source.zip")
$items = Get-ChildItem $release | Where-Object { $_.Name -ne "NACHO-$Version.zip" }
Compress-Archive -Path $items.FullName -DestinationPath (Join-Path $release "NACHO-$Version.zip")
Write-Host "Done: $release"
