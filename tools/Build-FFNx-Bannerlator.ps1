param(
    [string]$SourceDirectory = '.tools/FFNx-src',
    [string]$OutputDirectory = '.dist/FFNx-Bannerlator'
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$sourceRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot $SourceDirectory))
$outputRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot $OutputDirectory))
$cmake = 'C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe'
if (-not (Test-Path -LiteralPath $sourceRoot)) {
    throw 'Clone FFNx tag 1.24.3 into .tools/FFNx-src and initialize its vcpkg submodule first.'
}
if (-not (Test-Path -LiteralPath $cmake)) { throw 'Visual Studio 2026 Build Tools with CMake is required.' }

foreach ($patch in @('FFNx-Bannerlator-log.patch', 'FFNx-Bannerlator-toolset.patch')) {
    $patchPath = Join-Path $PSScriptRoot $patch
    & git -C $sourceRoot apply --reverse --check $patchPath 2>$null
    if ($LASTEXITCODE -ne 0) {
        & git -C $sourceRoot apply --check $patchPath
        if ($LASTEXITCODE -ne 0) { throw "Cannot apply $patch cleanly." }
        & git -C $sourceRoot apply $patchPath
        if ($LASTEXITCODE -ne 0) { throw "Applying $patch failed." }
    }
}

$ffmpegPort = Join-Path $sourceRoot '.vcpkg/ports/ffmpeg'
if (-not (Test-Path -LiteralPath $ffmpegPort)) {
    Copy-Item -LiteralPath (Join-Path $sourceRoot 'vcpkg/ports/ffmpeg') -Destination $ffmpegPort -Recurse
}
$portPatch = Join-Path $PSScriptRoot 'FFNx-ffmpeg-port.patch'
& git -C $ffmpegPort apply --reverse --check $portPatch 2>$null
if ($LASTEXITCODE -ne 0) {
    & git -C $ffmpegPort apply --check $portPatch
    if ($LASTEXITCODE -ne 0) { throw 'Cannot apply the FFmpeg port patch cleanly.' }
    & git -C $ffmpegPort apply $portPatch
    if ($LASTEXITCODE -ne 0) { throw 'Applying the FFmpeg port patch failed.' }
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'FFNx-ffmpeg-link-retry.patch') -Destination (Join-Path $ffmpegPort 'FFNx-ffmpeg-link-retry.patch') -Force

$vcpkg = Join-Path $sourceRoot 'vcpkg'
if (-not (Test-Path -LiteralPath (Join-Path $vcpkg 'vcpkg.exe'))) {
    & (Join-Path $vcpkg 'bootstrap-vcpkg.bat')
    if ($LASTEXITCODE -ne 0) { throw 'vcpkg bootstrap failed.' }
}

$buildRoot = Join-Path $sourceRoot '.build-bannerlator-short'
if (-not $sourceRoot.StartsWith($repoRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The FFNx source directory must be inside the 7th Heaven workspace.'
}
$relativeSource = $sourceRoot.Substring($repoRoot.Length + 1)
# x265's NASM invocation leaves its include path unquoted. A short drive path
# avoids that upstream build failure when the workspace path contains spaces.
if (Test-Path -LiteralPath 'B:\') { throw 'Drive B: is already in use.' }
& subst B: $repoRoot
if ($LASTEXITCODE -ne 0) { throw 'Could not create temporary B: build mapping.' }
try {
    $shortSource = Join-Path 'B:\' $relativeSource
    $shortBuild = Join-Path $shortSource '.build-bannerlator-short'
    $toolchain = Join-Path $shortSource 'vcpkg/scripts/buildsystems/vcpkg.cmake'
    $triplets = Join-Path $shortSource '.vcpkg/triplets'
    $ports = Join-Path $shortSource '.vcpkg/ports'
    & $cmake -S $shortSource -B $shortBuild -G 'Visual Studio 18 2026' -A Win32 -T host=x86 `
        '-DCMAKE_BUILD_TYPE=Release' '-D_DLL_VERSION=1.24.3.0' "-DCMAKE_TOOLCHAIN_FILE=$toolchain" `
        '-DVCPKG_TARGET_TRIPLET=x86-windows-static' "-DVCPKG_OVERLAY_TRIPLETS=$triplets" "-DVCPKG_OVERLAY_PORTS=$ports"
    if ($LASTEXITCODE -ne 0) { throw 'FFNx configure failed.' }
    & $cmake --build $shortBuild --config Release --parallel
    if ($LASTEXITCODE -ne 0) { throw 'FFNx build failed.' }
} finally {
    & subst B: /D
}

$driver = Join-Path $buildRoot 'bin/FFNx.dll'
if (-not (Test-Path -LiteralPath $driver)) { throw "Built driver missing: $driver" }
New-Item -ItemType Directory -Force -Path (Join-Path $outputRoot 'FFNx') | Out-Null
Copy-Item -LiteralPath $driver -Destination (Join-Path $outputRoot 'FFNx/AF3DN.P') -Force
Write-Output "Staged driver: $(Join-Path $outputRoot 'FFNx/AF3DN.P')"
