param(
    [Parameter(Mandatory = $true)]
    [string]
    $FunctionAppContentPath,

    [string]
    $InvokeCrankCommand = 'crank'
)

$ErrorActionPreference = 'Stop'

$crankConfigPath = Join-Path `
                    -Path (Split-Path $PSCommandPath -Parent) `
                    -ChildPath 'benchmarks.yml'

& $InvokeCrankCommand --config $crankConfigPath --scenario hello --profile local --variable FunctionAppContentPath=$FunctionAppContentPath
