param(
    [string]$PayloadDirectory = '.dist/7thHeaven-ARM-v0.7.0-beta-stage',
    [string]$Compiler = ''
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$payload = (Resolve-Path -LiteralPath (Join-Path $repo $PayloadDirectory)).Path

foreach ($required in @('7th Heaven.exe', '7th Heaven.dll', 'AppLoader.dll', 'AppProxy.dll',
                        'AppWrapper.dll', 'd3dcompiler_47.dll', '7zr-arm64.exe', '7zr-NOTICE.md', 'README.md', 'LICENSE.txt',
                        'runtime-x86/host/fxr')) {
    if (-not (Test-Path -LiteralPath (Join-Path $payload $required))) {
        throw "Installer payload is missing $required"
    }
}

if (-not $Compiler) {
    $candidates = @(
        (Join-Path $repo '.tools/InnoSetup6/ISCC.exe'),
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    )
    $Compiler = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (-not $Compiler -or -not (Test-Path -LiteralPath $Compiler)) {
    throw 'Inno Setup 6 ISCC.exe is required to build the installer.'
}

& $Compiler "/dPayloadDir=$payload" (Join-Path $repo '.iss/Bannerlator-installer.iss')
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed with exit code $LASTEXITCODE" }

$setup = Join-Path $repo '.dist/7thHeaven-ARM-v0.7.0-beta-setup.exe'
if (-not (Test-Path -LiteralPath $setup)) { throw "Expected installer missing: $setup" }
Get-FileHash -LiteralPath $setup -Algorithm SHA256 | Select-Object Path,Hash
