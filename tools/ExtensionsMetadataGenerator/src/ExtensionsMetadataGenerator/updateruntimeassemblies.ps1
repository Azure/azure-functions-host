# Update runtime assemblies based on current host definitions

$json = Get-Content '..\..\..\..\src\WebJobs.Script\runtimeassemblies.json' -raw 
$json = $json -replace '(?m)(?<=^([^"]|"[^"]*")*)//.*' -replace '(?ms)/\*.*?\*/'

$runtimeAssembliesJson = $json | ConvertFrom-Json
# Exclude assemblies with resolutionPolicy private
$result = $runtimeAssembliesJson.runtimeAssemblies |  Where-Object {$_.resolutionPolicy -ne "private"}

$files = $result | ForEach-Object {$_.name + '.dll'}
Set-Content -Path "runtimeAssemblies.txt" -Value $files