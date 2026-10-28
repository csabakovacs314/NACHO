# Builds a NACHO release into a NEW folder next to this source folder: <project folder>\NACHO-<version>
# (the project folder is the one that holds build-<version>, google-client and the releases, wherever it lives).
# Steps: icon, unit tests, portable and small exe, installer, self-test, source zip, package zip.
# Needs: .NET 10 SDK, Inno Setup 6 (winget install JRSoftware.InnoSetup --scope user).
# -LinuxDir: a folder with the Linux packages made by build-linux.sh; they go into the release's Linux folder.
# -GoogleClient: the Google "Desktop app" client file built into NACHO for Google Docs in place. It is read from
#   outside the source folder and never copied into the source zip. Without it, users load their own client file.
param([string]$Version = "0.12.0", [string]$LinuxDir = "",
      [string]$GoogleClient = "", [string]$ProjectDir = "")
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
if (-not $ProjectDir) { $ProjectDir = Split-Path $root -Parent }
if (-not $GoogleClient) { $GoogleClient = Join-Path $ProjectDir "google-client\client.json" }
$release = Join-Path $ProjectDir "NACHO-$Version"
if (Test-Path $release) { throw "Release folder already exists: $release. A new version needs a new folder." }
$iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe") |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup 6 not found. Install it: winget install JRSoftware.InnoSetup --scope user" }
$gcArg = @()
if ($GoogleClient -and (Test-Path $GoogleClient)) {
    $GoogleClient = (Resolve-Path $GoogleClient).Path
    if ($GoogleClient.StartsWith($root, [System.StringComparison]::OrdinalIgnoreCase)) { throw "Keep the Google client file outside the source folder." }
    $gcArg = @("-p:NachoGoogleClient=$GoogleClient")
    Write-Host "Google client: built in from $GoogleClient"
} else {
    Write-Host "Google client: none (users will need their own client file)"
}
if ($LinuxDir -and -not (Test-Path (Join-Path $LinuxDir "SHA256SUMS.txt"))) { throw "No Linux packages in $LinuxDir (run build-linux.sh on Linux first)." }

Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem

# Standard zips: forward slashes in paths (Compress-Archive in Windows PowerShell 5 writes backslashes,
# which Linux and macOS unpack as flat file names). The Unix modes are recorded too, but most Linux unzip
# tools ignore them in a zip made on Windows; the Linux tar.gz carries the real permissions.
function New-Zip([string]$Source, [string]$Zip, [string[]]$Skip = @()) {
    if (Test-Path $Zip) { Remove-Item $Zip }
    $base = (Resolve-Path $Source).Path.TrimEnd('\') + '\'
    $archive = [System.IO.Compression.ZipFile]::Open($Zip, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in Get-ChildItem $Source -Recurse -File) {
            if ($Skip -contains $file.FullName) { continue }
            $name = $file.FullName.Substring($base.Length).Replace('\', '/')
            $entry = [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $name, [System.IO.Compression.CompressionLevel]::Optimal)
            $mode = if ($file.Name -like "*.sh" -or $file.Name -eq "AppRun" -or $file.Name -like "*.AppImage") { 0x81ED } else { 0x81A4 }
            $entry.ExternalAttributes = $mode -shl 16
        }
    }
    finally {
        $archive.Dispose()
    }
}

Write-Host "1/7 Icon"
& (Join-Path $root "tools\make-icon.ps1") -Out (Join-Path $root "src\Nacho\nacho.ico")

Write-Host "2/7 Unit tests"
dotnet test (Join-Path $root "tests\Nacho.Tests\Nacho.Tests.csproj") -c Release -nologo
if ($LASTEXITCODE -ne 0) { throw "Unit tests failed." }

Write-Host "3/7 Publish portable, small and installer payload"
$out = Join-Path $root "out"
$app = Join-Path $root "src\Nacho\Nacho.csproj"
dotnet publish $app -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o (Join-Path $out "portable") -nologo @gcArg
if ($LASTEXITCODE -ne 0) { throw "Portable publish failed." }
dotnet publish $app -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true -o (Join-Path $out "small") -nologo @gcArg
if ($LASTEXITCODE -ne 0) { throw "Small publish failed." }
$payload = Join-Path $out "installer-payload"
if (Test-Path $payload) { Remove-Item $payload -Recurse -Force }
dotnet publish $app -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o $payload -nologo @gcArg
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
Copy-Item (Join-Path $root "licenses") (Join-Path $release "licenses") -Recurse
if ($LinuxDir) {
    $linux = Join-Path $release "Linux"
    New-Item -ItemType Directory -Path $linux -Force | Out-Null
    Copy-Item (Join-Path $LinuxDir "*.AppImage"), (Join-Path $LinuxDir "*.tar.gz"), (Join-Path $LinuxDir "SHA256SUMS.txt") $linux
}

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
foreach ($item in "src", "tests", "tools", "installer", "packaging", "licenses", "docs", "Directory.Build.props", "build.ps1", "build-linux.sh", "README.txt", "CHANGELOG.md") {
    Copy-Item (Join-Path $root $item) $stage -Recurse
}
Get-ChildItem $stage -Recurse -Directory | Where-Object { $_.Name -in "bin", "obj" } | Remove-Item -Recurse -Force
New-Zip $stage (Join-Path $release "NACHO-$Version-source.zip")
$package = Join-Path $out "NACHO-$Version.zip"
New-Zip $release $package
Move-Item $package (Join-Path $release "NACHO-$Version.zip")
Write-Host "Done: $release"
