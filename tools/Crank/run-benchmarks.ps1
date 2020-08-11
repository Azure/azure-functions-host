param(
    [string]
    $FunctionsHostBranchOrCommit = 'dev',

    [string]
    $FunctionApp = 'HelloApp',

    [string]
    $InvokeCrankCommand,

    [switch]
    $WriteResultsToDatabase,

    [switch]
    $RefreshCrankContoller
)

$ErrorActionPreference = 'Stop'

#region Utilities

function InstallCrankController {
    dotnet tool install -g Microsoft.Crank.Controller --version "0.1.0-*"
}

function UninstallCrankController {
    dotnet tool uninstall -g microsoft.crank.controller
}

#endregion

#region Main

if (-not $InvokeCrankCommand) {
    if (Get-Command crank -ErrorAction SilentlyContinue) {
        if ($RefreshCrankContoller) {
            Write-Warning 'Reinstalling crank controller...'
            UninstallCrankController
            InstallCrankController
        }
    } else {
        Write-Warning 'Crank controller is not found, installing...'
        InstallCrankController
    }
    $InvokeCrankCommand = 'crank'
}

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

#endregion
