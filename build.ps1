# Builds a NACHO release into a NEW folder: Downloads\NACHO\NACHO-<version>
# Steps: icon, unit tests, portable and small exe, installer, self-test, source zip, package zip.
# Needs: .NET 10 SDK, Inno Setup 6 (winget install JRSoftware.InnoSetup --scope user).
param([string]$Version = "0.2.0")
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$release = Join-Path $env:USERPROFILE "Downloads\NACHO\NACHO-$Version"
if (Test-Path $release) { throw "Release folder already exists: $release. A new version needs a new folder." }
$iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe") |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup 6 not found. Install it: winget install JRSoftware.InnoSetup --scope user" }

Write-Host "1/7 Icon"
& (Join-Path $root "tools\make-icon.ps1") -Out (Join-Path $root "src\Nacho\nacho.ico")

Write-Host "2/7 Unit tests"
dotnet test (Join-Path $root "tests\Nacho.Tests\Nacho.Tests.csproj") -c Release -nologo
if ($LASTEXITCODE -ne 0) { throw "Unit tests failed." }

Write-Host "3/7 Publish portable, small and installer payload"
$out = Join-Path $root "out"
$app = Join-Path $root "src\Nacho\Nacho.csproj"
dotnet publish $app -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o (Join-Path $out "portable") -nologo
if ($LASTEXITCODE -ne 0) { throw "Portable publish failed." }
dotnet publish $app -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true -o (Join-Path $out "small") -nologo
if ($LASTEXITCODE -ne 0) { throw "Small publish failed." }
$payload = Join-Path $out "installer-payload"
if (Test-Path $payload) { Remove-Item $payload -Recurse -Force }
dotnet publish $app -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o $payload -nologo
if ($LASTEXITCODE -ne 0) { throw "Installer payload publish failed." }
Get-ChildItem $payload -Filter *.pdb | Remove-Item

Write-Host "4/7 Installer"
& $iscc /Q (Join-Path $root "installer\nacho.iss")
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed." }

Write-Host "5/7 Release folder"
New-Item -ItemType Directory -Path (Join-Path $release "Small") -Force | Out-Null
Copy-Item (Join-Path $out "installer\NACHO-$Version-setup.exe") $release
Copy-Item (Join-Path $out "portable\NACHO.exe") $release
Copy-Item (Join-Path $out "small\NACHO.exe") (Join-Path $release "Small")
Copy-Item (Join-Path $root "README.txt"), (Join-Path $root "CHANGELOG.md") $release

Write-Host "6/7 Self-test both exes from the release folder"
foreach ($exe in @((Join-Path $release "NACHO.exe"), (Join-Path $release "Small\NACHO.exe"))) {
    $dir = Join-Path $out ("selftest-" + (Split-Path (Split-Path $exe -Parent) -Leaf))
    $p = Start-Process -FilePath $exe -ArgumentList "--selftest", "`"$dir`"" -Wait -PassThru
    if ($p.ExitCode -ne 0) { throw "Self-test failed for $exe (exit $($p.ExitCode)). See $dir\selftest-report.txt" }
}

Write-Host "7/7 Zips"
$stage = Join-Path $out "source-stage"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Path $stage | Out-Null
foreach ($item in "src", "tests", "tools", "installer", "Directory.Build.props", "build.ps1", "README.txt", "CHANGELOG.md") {
    Copy-Item (Join-Path $root $item) $stage -Recurse
}
Get-ChildItem $stage -Recurse -Directory | Where-Object { $_.Name -in "bin", "obj" } | Remove-Item -Recurse -Force
Compress-Archive -Path (Join-Path $stage "*") -DestinationPath (Join-Path $release "NACHO-$Version-source.zip")
$items = Get-ChildItem $release | Where-Object { $_.Name -ne "NACHO-$Version.zip" }
Compress-Archive -Path $items.FullName -DestinationPath (Join-Path $release "NACHO-$Version.zip")
Write-Host "Done: $release"
