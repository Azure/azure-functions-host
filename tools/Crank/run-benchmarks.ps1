param(
    [string]
    $FunctionsHostBranchOrCommit = 'dev',

    [string]
    $FunctionApp = 'HelloApp',

    [string]
    $InvokeCrankCommand = 'crank',

    [switch]
    $WriteResultsToDatabase
)

$ErrorActionPreference = 'Stop'

$crankConfigPath = Join-Path `
                    -Path (Split-Path $PSCommandPath -Parent) `
                    -ChildPath 'benchmarks.yml'

if ($WriteResultsToDatabase) {
    Set-AzContext -Subscription 'Antares-Demo' > $null
    $sqlPassword = (Get-AzKeyVaultSecret -vaultName 'functions-crank-kv' -name 'SqlAdminPassword').SecretValueText

    $sqlConnectionString = "Server=tcp:functions-crank-sql.database.windows.net,1433;Initial Catalog=functions-crank-db;Persist Security Info=False;User ID=Functions;Password=$sqlPassword;MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;"

    & $InvokeCrankCommand --config $crankConfigPath --scenario functionApp --profile local --variable FunctionApp=$FunctionApp --variable FunctionsHostBranchOrCommit=$FunctionsHostBranchOrCommit --sql $sqlConnectionString --table FunctionsPerf
} else {
    & $InvokeCrankCommand --config $crankConfigPath --scenario functionApp --profile local --variable FunctionApp=$FunctionApp --variable FunctionsHostBranchOrCommit=$FunctionsHostBranchOrCommit
}
