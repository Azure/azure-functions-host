param(
    [string]
    $FunctionApp = 'HelloApp',

    [string]
    $InvokeCrankCommand = 'crank'
)

$ErrorActionPreference = 'Stop'

$crankConfigPath = Join-Path `
                    -Path (Split-Path $PSCommandPath -Parent) `
                    -ChildPath 'benchmarks.yml'

& $InvokeCrankCommand --config $crankConfigPath --scenario functionApp --profile local --variable FunctionApp=$FunctionApp
