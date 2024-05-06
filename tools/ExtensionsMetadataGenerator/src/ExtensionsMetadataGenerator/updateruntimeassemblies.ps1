# Update runtime assemblies based on current host definitions
function UpdateRuntimeAssemblies
{
    param (
        [Parameter(Mandatory=$true)]
        [ValidateNotNullOrEmpty()]
        [System.String]
        $TargetFramework
    )

    $sourcePath = '..\..\..\..\src\WebJobs.Script\runtimeassemblies-' + $TargetFramework + '.json'
    $json = Get-Content $sourcePath -raw 
    $json = $json -replace '(?m)(?<=^([^"]|"[^"]*")*)//.*' -replace '(?ms)/\*.*?\*/'

    $runtimeAssembliesJson = $json | ConvertFrom-Json
    # Exclude assemblies with resolutionPolicy private
    $result = $runtimeAssembliesJson.runtimeAssemblies |  Where-Object {$_.resolutionPolicy -ne "private"}

    $files = $result | ForEach-Object {$_.name + '.dll'}
    $targetName = 'runtimeAssemblies-' + $TargetFramework + '.txt'
    Set-Content -Path $targetName  -Value $files
}

UpdateRuntimeAssemblies -TargetFramework "net6"
UpdateRuntimeAssemblies -TargetFramework "net8"