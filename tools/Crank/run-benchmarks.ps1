param(
    [Parameter(Mandatory = $true)]
    [string]
    $FunctionAppContentPath,

    [string]
    $InvokeCrankCommand = 'crank'
)

$ErrorActionPreference = 'Stop'

& $InvokeCrankCommand --config '.\benchmarks.yml' --scenario hello --profile local --variable FunctionAppContentPath=$FunctionAppContentPath
