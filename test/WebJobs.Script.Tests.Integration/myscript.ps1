# Get all subdirectories of the destination root
$subDirs = Get-ChildItem -Path "..\TestWorkers\Probingpaths\workers\java" -Recurse -Directory

foreach ($dir in $subDirs) {
    Write-Host "Copying to $($dir.FullName)"
    Copy-Item -Path "..\..\out\bin\WebJobs.Script.Tests.Integration\debug\workers\java\*" -Destination $dir.FullName -Recurse -Force
}

# Get all subdirectories of the destination root
$subDirsNode = Get-ChildItem -Path "..\TestWorkers\Probingpaths\workers\node" -Recurse -Directory

foreach ($dir in $subDirsNode) {
    Write-Host "Copying to $($dir.FullName)"
    Copy-Item -Path "..\..\out\bin\WebJobs.Script.Tests.Integration\debug\workers\node\*" -Destination $dir.FullName -Recurse -Force
}
