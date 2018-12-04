
function RunTest([string] $project, [string] $description,[bool] $skipBuild = $false, $filter = $null) {
    Write-Host "Running test: $description" -ForegroundColor DarkCyan
    Write-Host "-----------------------------------------------------------------------------" -ForegroundColor DarkCyan
    Write-Host

    $cmdargs = "test", ".\test\$project\", "-v", "q"
    
    if ($filter) {
       $cmdargs += "--filter", "$filter"
    }

# We'll always rebuild for now.
#    if ($skipBuild){
#        $cmdargs += "--no-build"
#    }
#    else {
#        Write-Host "Rebuilding project" -ForegroundColor Red
#    }
    
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
  @{project ="WebJobs.Script.Tests.Integration"; description="C# end to end tests"; filter ="Group=CSharpEndToEndTests"},
  @{project ="WebJobs.Script.Tests.Integration"; description="Node end to end tests"; filter ="Group=NodeEndToEndTests"},
  @{project ="WebJobs.Script.Tests.Integration"; description="Direct load end to end tests"; filter ="Group=DirectLoadEndToEndTests"},
  @{project ="WebJobs.Script.Tests.Integration"; description="F# end to end tests"; filter ="Group=FSharpEndToEndTests"},
  @{project ="WebJobs.Script.Tests.Integration"; description="Language worker end to end tests"; filter ="Group=LanguageWorkerSelectionEndToEndTests"},
  @{project ="WebJobs.Script.Tests.Integration"; description="Node script host end to end tests"; filter ="Group=NodeScriptHostTests"},
  @{project ="WebJobs.Script.Tests.Integration"; description="Raw assembly end to end tests"; filter ="Group=RawAssemblyEndToEndTests"},
  @{project ="WebJobs.Script.Tests.Integration"; description="Samples end to end tests"; filter ="Group=SamplesEndToEndTests"}
  @{project ="WebJobs.Script.Tests.Integration"; description="Standby mode end to end tests Windows"; filter ="Group=StandbyModeEndToEndTests_Windows"}
  @{project ="WebJobs.Script.Tests.Integration"; description="Standby mode end to end tests Linux"; filter ="Group=StandbyModeEndToEndTests_Linux"}
  @{project ="WebJobs.Script.Tests.Integration"; description="Linux Container end to end tests"; filter ="Group=ContainerInstanceTests"}
)

$success = $true
$testRunSucceeded = $true

foreach ($test in $tests){
    $testRunSucceeded = RunTest $test.project $test.description $testRunSucceeded $test.filter
    $success = $testRunSucceeded -and $success
}

if (-not $success) { exit 1 }