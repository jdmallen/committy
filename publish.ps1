# Publish the .NET application for multiple runtimes
# Cleans up extraneous files, keeping only the executable

$runtimes = @("win-x64", "linux-x64", "osx-x64", "osx-arm64")
$outputDir = "./dist"

foreach ($runtime in $runtimes) {
    $runtimeDir = "$outputDir/$runtime"
    Write-Host "Publishing for $runtime..."
    dotnet publish -c Release -r $runtime -o $runtimeDir
    
    Write-Host "Cleaning up $runtimeDir..."
    
    if ($runtime -eq "win-x64") {
        # On Windows, keep only committy.exe
        Get-ChildItem -Path $runtimeDir -Recurse -File | 
            Where-Object { $_.Name -ne "committy.exe" } | 
            Remove-Item -Force
    } else {
        # On Unix-like systems, keep only committy (no extension)
        Get-ChildItem -Path $runtimeDir -Recurse -File | 
            Where-Object { $_.Name -ne "committy" } | 
            Remove-Item -Force
    }
    
    # Remove empty directories
    Get-ChildItem -Path $runtimeDir -Recurse -Directory | 
        Where-Object { @(Get-ChildItem -Path $_.FullName -ErrorAction SilentlyContinue).Count -eq 0 } | 
        Remove-Item -Force
    
    # Get directory size for display
    $size = (Get-ChildItem -Path $runtimeDir -Recurse | Measure-Object -Property Length -Sum).Sum
    $sizeDisplay = if ($size -lt 1MB) { "$([math]::Round($size/1KB, 2)) KB" } else { "$([math]::Round($size/1MB, 2)) MB" }
    Write-Host "✓ $runtime`: $sizeDisplay"
}

Write-Host ""
Write-Host "Done! Executables are in $outputDir"
Get-ChildItem -Path "$outputDir" -Recurse -File | ForEach-Object { Write-Host $_.FullName }
