Remove-Item -Path build\Downio\*.pdb -Force

if (-not (Test-Path "build\Downio\aria2c.exe" -PathType Leaf)) {
    throw "Missing aria2c.exe in the Windows publish root for $env:RUNTIME."
}

if ($env:RUNTIME -eq "win7-x64") {
    # Downio.exe links msvcrt via VC-LTL, but aria2c.exe needs the UCRT, which Windows 7 only has
    # with KB2999226. Ship it app-local; Windows 10+ ignores these copies in favour of its own.
    $kits = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\Redist"
    $ucrt = Get-ChildItem -Path $kits -Directory -Recurse -Filter x64 -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -like "*\ucrt\DLLs\x64" -and (Test-Path (Join-Path $_.FullName "ucrtbase.dll")) } |
        Sort-Object FullName -Descending | Select-Object -First 1
    if (-not $ucrt) {
        throw "UCRT redistributable not found under $kits."
    }
    Copy-Item -Path (Join-Path $ucrt.FullName "*.dll") -Destination build\Downio -Force
}

Compress-Archive -Path build\Downio -DestinationPath "build\Downio_${env:VERSION}.${env:RUNTIME}.zip" -Force
