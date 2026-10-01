param(
    [string]$OutputDirectory = ".dist/Bannerlator",
    [string]$RuntimeVersion = "10.0.11",
    [ValidateSet("win-x64", "win-arm64")]
    [string]$UiRuntimeIdentifier = "win-x64",
    [string]$Arm64D3DCompilerPath = "",
    [string]$PrivateRuntimeSource = "",
    [switch]$NoRestore
)

$ErrorActionPreference = "Stop"
$repo = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$output = [System.IO.Path]::GetFullPath((Join-Path $repo $OutputDirectory))
$nativeOutput = Join-Path $repo "AppUI/bin/Release/net10.0-windows7.0"
$loader = Join-Path $nativeOutput "AppLoader.dll"

if (-not (Test-Path -LiteralPath $loader)) {
    throw "Build the Release solution, including the Win32 AppLoader project, before publishing. Missing: $loader"
}

$dotnetCommand = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnetCommand -or -not (& $dotnetCommand --list-sdks)) {
    $dotnetCommand = Join-Path $repo ".tools/dotnet2/dotnet.exe"
}
if (-not (Test-Path -LiteralPath $dotnetCommand) -or -not (& $dotnetCommand --list-sdks)) {
    throw "The .NET 10 SDK is required on the build machine."
}

if (-not $NoRestore) {
    & $dotnetCommand restore (Join-Path $repo "AppUI/AppUI.csproj") -r $UiRuntimeIdentifier -p:BannerlatorBuild=true
    if ($LASTEXITCODE -ne 0) { throw "Restoring 7th Heaven dependencies failed." }
}

& $dotnetCommand publish (Join-Path $repo "AppUI/AppUI.csproj") -c Release -r $UiRuntimeIdentifier --self-contained true --no-restore `
    -p:BannerlatorBuild=true -p:PublishSingleFile=false -p:PublishTrimmed=false -o $output
if ($LASTEXITCODE -ne 0) { throw "Publishing 7th Heaven failed." }

if ($UiRuntimeIdentifier -eq "win-arm64") {
    if (-not $Arm64D3DCompilerPath) {
        $Arm64D3DCompilerPath = Join-Path ${env:ProgramFiles(x86)} "Windows Kits/10/Redist/D3D/arm64/d3dcompiler_47.dll"
    }
    if (-not (Test-Path -LiteralPath $Arm64D3DCompilerPath)) {
        throw "The ARM64 WPF runtime needs d3dcompiler_47.dll. Install the Windows 10 SDK or pass -Arm64D3DCompilerPath. Missing: $Arm64D3DCompilerPath"
    }
    $compilerBytes = [System.IO.File]::ReadAllBytes($Arm64D3DCompilerPath)
    $peOffset = [BitConverter]::ToInt32($compilerBytes, 0x3c)
    if ([BitConverter]::ToUInt16($compilerBytes, $peOffset + 4) -ne 0xAA64) {
        throw "The selected d3dcompiler_47.dll is not ARM64: $Arm64D3DCompilerPath"
    }
    Copy-Item -LiteralPath $Arm64D3DCompilerPath -Destination (Join-Path $output "d3dcompiler_47.dll") -Force

    $extractor = Join-Path $repo "third_party/lzma-sdk/7zr-arm64.exe"
    if (-not (Test-Path -LiteralPath $extractor)) { throw "Missing ARM64 7z extractor: $extractor" }
    $extractorBytes = [System.IO.File]::ReadAllBytes($extractor)
    $extractorPeOffset = [BitConverter]::ToInt32($extractorBytes, 0x3c)
    if ([BitConverter]::ToUInt16($extractorBytes, $extractorPeOffset + 4) -ne 0xAA64) {
        throw "The bundled 7z extractor is not ARM64: $extractor"
    }
    Copy-Item -LiteralPath $extractor -Destination (Join-Path $output "7zr-arm64.exe") -Force
    Copy-Item -LiteralPath (Join-Path $repo "third_party/lzma-sdk/README.md") -Destination (Join-Path $output "7zr-NOTICE.md") -Force
}

foreach ($name in @("AppLoader.dll", "nethost.dll", "AppProxy.runtimeconfig.json")) {
    $source = Join-Path $nativeOutput $name
    if (-not (Test-Path -LiteralPath $source)) { throw "Missing native/game-side component: $source" }
    Copy-Item -LiteralPath $source -Destination (Join-Path $output $name) -Force
}

$proxyConfigPath = Join-Path $output "AppProxy.runtimeconfig.json"
$proxyConfig = Get-Content -LiteralPath $proxyConfigPath -Raw | ConvertFrom-Json
if (-not $proxyConfig.runtimeOptions.configProperties) {
    $proxyConfig.runtimeOptions | Add-Member -NotePropertyName configProperties -NotePropertyValue ([pscustomobject]@{})
}
$proxyConfig.runtimeOptions.configProperties | Add-Member -NotePropertyName "System.Globalization.UseNls" -NotePropertyValue $true -Force
$proxyConfig | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $proxyConfigPath -Encoding utf8

$privateRuntime = Join-Path $output "runtime-x86"
if ($PrivateRuntimeSource) {
    $source = (Resolve-Path -LiteralPath $PrivateRuntimeSource).Path
    if (-not (Test-Path -LiteralPath (Join-Path $source "host/fxr"))) { throw "Invalid private x86 runtime: $source" }
    New-Item -ItemType Directory -Path $privateRuntime -Force | Out-Null
    Copy-Item -Path (Join-Path $source '*') -Destination $privateRuntime -Recurse -Force
} else {
    New-Item -ItemType Directory -Path $privateRuntime -Force | Out-Null
    $installer = Join-Path $env:TEMP "7h-dotnet-install.ps1"
    Invoke-WebRequest "https://dot.net/v1/dotnet-install.ps1" -OutFile $installer
    & $installer -Runtime dotnet -Version $RuntimeVersion -Architecture x86 -InstallDir $privateRuntime -NoPath
    if ($LASTEXITCODE -ne 0) { throw "Installing the private x86 .NET runtime failed." }
}
if (-not (Test-Path -LiteralPath (Join-Path $privateRuntime "host/fxr"))) { throw "Private x86 .NET runtime is missing." }

Copy-Item -LiteralPath (Join-Path $repo "README.md") -Destination $output -Force
Copy-Item -LiteralPath (Join-Path $repo "LICENSE.txt") -Destination $output -Force
$readmeScreenshot = Join-Path $repo "docs/images/FF7-7h-ARM-Bannerlator.png"
if (-not (Test-Path -LiteralPath $readmeScreenshot)) { throw "Missing README screenshot: $readmeScreenshot" }
$imageOutput = Join-Path $output "docs/images"
New-Item -ItemType Directory -Path $imageOutput -Force | Out-Null
Copy-Item -LiteralPath $readmeScreenshot -Destination $imageOutput -Force

Write-Host "Bannerlator package ready: $output"
