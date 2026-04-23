Remove-Item -Path build\Downio\*.pdb -Force

# Cleanup binaries
# Keep only win32
Get-ChildItem -Path "build\Downio\Assets\Binaries" -Directory | Where-Object { $_.Name -ne "win32" } | Remove-Item -Recurse -Force
if ($env:RUNTIME -eq "win-x64") {
    Remove-Item -Path "build\Downio\Assets\Binaries\win32\ia32" -Recurse -Force
} elseif ($env:RUNTIME -eq "win-arm64") {
    Remove-Item -Path "build\Downio\Assets\Binaries\win32\x64" -Recurse -Force
    Remove-Item -Path "build\Downio\Assets\Binaries\win32\ia32" -Recurse -Force
}

Compress-Archive -Path build\Downio -DestinationPath "build\Downio_${env:VERSION}.${env:RUNTIME}.zip" -Force
