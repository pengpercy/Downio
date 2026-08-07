Remove-Item -Path build\Downio\*.pdb -Force

if (-not (Test-Path "build\Downio\aria2c.exe" -PathType Leaf)) {
    throw "Missing aria2c.exe in the Windows publish root for $env:RUNTIME."
}

Compress-Archive -Path build\Downio -DestinationPath "build\Downio_${env:VERSION}.${env:RUNTIME}.zip" -Force
