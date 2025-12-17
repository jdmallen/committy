# Publish the .NET application for multiple runtimes

runtimes = @("win-x64", "linux-x64", "osx-x64", "osx-arm64")
$outputDir = "./dist"

foreach ($runtime in $runtimes) {
  Write-Host "Publishing for $runtime..."
  dotnet publish -c Release -r $runtime -o "$outputDir/$runtime"
}

Write-Host "Done! Executables are in $outputDir"
