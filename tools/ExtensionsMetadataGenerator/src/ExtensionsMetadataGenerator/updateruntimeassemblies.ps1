# Update runtime assemblies based on current host definitions

$json = Get-Content '..\..\..\..\src\WebJobs.Script\runtimeassemblies.json' -raw 
$json = $json -replace '(?m)(?<=^([^"]|"[^"]*")*)//.*' -replace '(?ms)/\*.*?\*/'
$result = $json | ConvertFrom-Json

$files = $result.runtimeAssemblies | ForEach-Object {$_.name + '.dll'}

Set-Content -Path "runtimeAssemblies.txt" -Value $files