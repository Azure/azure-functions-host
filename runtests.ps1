param (
    [System.String]
    $Configuration = "Debug",
    [System.String]
    $ResultsPath = ".\testoutput\xunit\"
)

if (-not (Test-Path -Path $ResultsPath -IsValid)) 
{
    throw "Parameter {0}: '$ResultsPath' is not a valid path. Exiting script." -f '$ResultsPath'
}

Write-Host "Running tests. Test results will be written to: $ResultsPath"

$exitCode = 0;
$consoleRunnerx86Path = "$env:BUILD_REPOSITORY_LOCALPATH\packages\xunit.runner.console.2.3.0\tools\net452\xunit.console.x86.exe"
$consoleRunnerPath = "$env:BUILD_REPOSITORY_LOCALPATH\packages\xunit.runner.console.2.3.0\tools\net452\xunit.console.exe"
function CheckExitCode([string] $step,[int] $currentCode)
{
    if ($LASTEXITCODE -ne 0)
    {
        Write-Host "Step '$step' failed" -ForegroundColor Red
        return 1
    }

    return $currentCode;
}

function GetResultsPath([string] $trait)
{
    $path = Join-Path -Path $ResultsPath -ChildPath "xunit-$($trait)Tests.xml";
    return $path;
}

& $consoleRunnerx86Path "$env:BUILD_REPOSITORY_LOCALPATH\test\WebJobs.Script.Tests\bin\Release\Microsoft.Azure.WebJobs.Script.Tests.dll" -verbose -xml "$(GetResultsPath('Unit'))"
$exitCode = CheckExitCode "Unit tests" $exitCode

.\runNodeTests.cmd $Configuration
$exitCode = CheckExitCode "Node tests" $exitCode

& $consoleRunnerx86Path "$env:BUILD_REPOSITORY_LOCALPATH\test\WebJobs.Script.Tests.Integration\bin\Release\Microsoft.Azure.WebJobs.Script.Tests.Integration.dll" -notrait "Category=E2E" -verbose -xml "$(GetResultsPath('E2E'))";
$exitCode = CheckExitCode "Non-E2E tests" $exitCode

& $consoleRunnerx86Path "$env:BUILD_REPOSITORY_LOCALPATH\test\WebJobs.Script.Tests.Integration\bin\Release\Microsoft.Azure.WebJobs.Script.Tests.Integration.dll" -trait "E2E=ScriptHostManagerTests" -verbose -xml "$(GetResultsPath('ScriptHostManager'))";
$exitCode = CheckExitCode "ScriptHostManagerTests tests" $exitCode

& $consoleRunnerx86Path "$env:BUILD_REPOSITORY_LOCALPATH\test\WebJobs.Script.Tests.Integration\bin\Release\Microsoft.Azure.WebJobs.Script.Tests.Integration.dll" -trait "E2E=EndToEndTimeoutTests" -verbose -xml "$(GetResultsPath('EndToEndTimeout'))";
$exitCode = CheckExitCode "EndToEndTimeoutTests tests" $exitCode

& $consoleRunnerx86Path "$env:BUILD_REPOSITORY_LOCALPATH\test\WebJobs.Script.Tests.Integration\bin\Release\Microsoft.Azure.WebJobs.Script.Tests.Integration.dll" -trait "E2E=CSharpEndToEndTests" -verbose -xml "$(GetResultsPath('CSharpEndToEnd'))";
$exitCode = CheckExitCode "CSharpEndToEndTests tests" $exitCode

& $consoleRunnerx86Path "$env:BUILD_REPOSITORY_LOCALPATH\test\WebJobs.Script.Tests.Integration\bin\Release\Microsoft.Azure.WebJobs.Script.Tests.Integration.dll" -trait "E2E=NodeEndToEndTests" -verbose -xml "$(GetResultsPath('NodeEndToEnd'))";
$exitCode = CheckExitCode "NodeEndToEndTests tests" $exitCode

& $consoleRunnerx86Path "$env:BUILD_REPOSITORY_LOCALPATH\test\WebJobs.Script.Tests.Integration\bin\Release\Microsoft.Azure.WebJobs.Script.Tests.Integration.dll" -trait "E2E=BashEndToEndTests" -verbose -xml "$(GetResultsPath('BashEndToEnd'))";
$exitCode = CheckExitCode "BashEndToEndTests tests" $exitCode

& $consoleRunnerx86Path "$env:BUILD_REPOSITORY_LOCALPATH\test\WebJobs.Script.Tests.Integration\bin\Release\Microsoft.Azure.WebJobs.Script.Tests.Integration.dll" -trait "E2E=DirectLoadEndToEndTests" -verbose -xml "$(GetResultsPath('DirectLoadEndToEnd'))";
$exitCode = CheckExitCode "DirectLoadEndToEndTests tests" $exitCode

& $consoleRunnerx86Path "$env:BUILD_REPOSITORY_LOCALPATH\test\WebJobs.Script.Tests.Integration\bin\Release\Microsoft.Azure.WebJobs.Script.Tests.Integration.dll" -trait "E2E=FSharpEndToEndTests" -verbose -xml "$(GetResultsPath('FSharpEndToEnd'))";
$exitCode = CheckExitCode "FSharpEndToEndTests tests" $exitCode

& $consoleRunnerx86Path "$env:BUILD_REPOSITORY_LOCALPATH\test\WebJobs.Script.Tests.Integration\bin\Release\Microsoft.Azure.WebJobs.Script.Tests.Integration.dll" -trait "E2E=PowerShellEndToEndTests" -verbose -xml "$(GetResultsPath('PowerShellEndToEnd'))";
$exitCode = CheckExitCode "PowerShellEndToEndTests tests" $exitCode

& $consoleRunnerx86Path "$env:BUILD_REPOSITORY_LOCALPATH\test\WebJobs.Script.Tests.Integration\bin\Release\Microsoft.Azure.WebJobs.Script.Tests.Integration.dll" -trait "E2E=PowerShellEndToEndTests" -verbose -xml "$(GetResultsPath('PowerShellEndToEnd'))";
$exitCode = CheckExitCode "PythonEndToEndTests tests" $exitCode

& $consoleRunnerx86Path "$env:BUILD_REPOSITORY_LOCALPATH\test\WebJobs.Script.Tests.Integration\bin\Release\Microsoft.Azure.WebJobs.Script.Tests.Integration.dll" -trait "E2E=RawAssemblyEndToEndTests" -verbose -xml "$(GetResultsPath('RawAssemblyEndToEnd'))";
$exitCode = CheckExitCode "RawAssemblyEndToEndTests tests" $exitCode

& $consoleRunnerx86Path "$env:BUILD_REPOSITORY_LOCALPATH\test\WebJobs.Script.Tests.Integration\bin\Release\Microsoft.Azure.WebJobs.Script.Tests.Integration.dll" -trait "E2E=SamplesEndToEndTests" -verbose -xml "$(GetResultsPath('SamplesEndToEnd'))";
$exitCode = CheckExitCode "SamplesEndToEndTests tests" $exitCode

& $consoleRunnerPath "$env:BUILD_REPOSITORY_LOCALPATH\test\WebJobs.Script.Tests.Integration\bin\Release\Microsoft.Azure.WebJobs.Script.Tests.Integration.dll" -trait "E2E=NodeEndToEndTests" -verbose -xml "$(GetResultsPath('NodeEndToEndX64'))";
$exitCode = CheckExitCode "NodeEndToEndTests tests (x64)" $exitCode

Write-Host "Completed test with with exit code $exitCode"

exit $exitCode