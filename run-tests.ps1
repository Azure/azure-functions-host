
function RunTest([string] $project, [string] $description,[bool] $skipBuild = $false, $filter = $null) {
    Write-Host "Running test: $description" -ForegroundColor DarkCyan
    Write-Host "-----------------------------------------------------------------------------" -ForegroundColor DarkCyan
    Write-Host

    $cmdargs = "test", ".\test\$project\", "-v", "n"
    
    if ($filter) {
       $cmdargs += "--filter", "$filter"
    }

    if ($skipBuild){
        $cmdargs += "--no-build"
    }
    else {
        Write-Host "Rebuilding project" -ForegroundColor Red
    }
    
    & dotnet $cmdargs | Out-Host
    $r = $?
    
    Write-Host
    Write-Host "-----------------------------------------------------------------------------" -ForegroundColor DarkCyan
    Write-Host

    return $r
}


$tests = @(
  @{project ="WebJobs.Script.Tests"; description="Unit Tests"},
  @{project ="WebJobs.Script.Scaling.Tests"; description="Scaling Tests"},
  @{project ="WebJobs.Script.Tests.Integration"; description="Non-E2E integration tests"; filter ="Category!=E2E"},
  @{project ="WebJobs.Script.Tests.Integration"; description="C# end to end tests"; filter ="E2E=CSharpEndToEndTests"},
  @{project ="WebJobs.Script.Tests.Integration"; description="Node end to end tests"; filter ="E2E=NodeEndToEndTests"},
  @{project ="WebJobs.Script.Tests.Integration"; description="Direct load end to end tests"; filter ="E2E=DirectLoadEndToEndTests"},
  @{project ="WebJobs.Script.Tests.Integration"; description="F# end to end tests"; filter ="E2E=FSharpEndToEndTests"},
  @{project ="WebJobs.Script.Tests.Integration"; description="Language worker end to end tests"; filter ="E2E=LanguageWorkerSelectionEndToEndTests"},
  @{project ="WebJobs.Script.Tests.Integration"; description="Node script host end to end tests"; filter ="E2E=NodeScriptHostTests"},
  @{project ="WebJobs.Script.Tests.Integration"; description="Raw assembly end to end tests"; filter ="E2E=RawAssemblyEndToEndTests"},
  @{project ="WebJobs.Script.Tests.Integration"; description="Samples end to end tests"; filter ="E2E=SamplesEndToEndTests"}
)

$success = $true
$testRunSucceeded = $true

foreach ($test in $tests){
    $testRunSucceeded = RunTest $test.project $test.description $testRunSucceeded $test.filter
    $success = $testRunSucceeded -and $success
}

if (-not $success) { exit 1 }
