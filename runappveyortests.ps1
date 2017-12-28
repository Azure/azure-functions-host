$exitCode = 0;
$consoleRunnerx86Path = "$env:APPVEYOR_BUILD_FOLDER\packages\xunit.runner.console.2.3.0\tools\net452\xunit.console.x86.exe"
$consoleRunnerPath = "$env:APPVEYOR_BUILD_FOLDER\packages\xunit.runner.console.2.3.0\tools\net452\xunit.console.exe"
function CheckExitCode([string] $step,[int] $currentCode)
{
    if ($LASTEXITCODE -ne 0)
    {
        Write-Host "Step '$step' failed" -ForegroundColor Red
        return 1
    }

    return $currentCode;
}

& $consoleRunnerx86Path "$env:APPVEYOR_BUILD_FOLDER\test\WebJobs.Script.Tests\bin\Release\Microsoft.Azure.WebJobs.Script.Tests.dll" -appveyor
$exitCode = CheckExitCode "Unit tests" $exitCode

& $consoleRunnerx86Path "$env:APPVEYOR_BUILD_FOLDER\test\WebJobs.Script.Scaling.Tests\bin\Release\Microsoft.Azure.WebJobs.Script.Scaling.Tests.dll" -appveyor
$exitCode = CheckExitCode "Scaling tests" $exitCode

.\runNodeTests.cmd
$exitCode = CheckExitCode "Node tests" $exitCode

& $consoleRunnerx86Path "$env:APPVEYOR_BUILD_FOLDER\test\WebJobs.Script.Tests.Integration\bin\Release\Microsoft.Azure.WebJobs.Script.Tests.Integration.dll" -notrait "Category=E2E"
$exitCode = CheckExitCode "Non-E2E tests" $exitCode

& $consoleRunnerx86Path "$env:APPVEYOR_BUILD_FOLDER\test\WebJobs.Script.Tests.Integration\bin\Release\Microsoft.Azure.WebJobs.Script.Tests.Integration.dll" -trait "E2E=ScriptHostManagerTests"
$exitCode = CheckExitCode "ScriptHostManagerTests tests" $exitCode

& $consoleRunnerx86Path "$env:APPVEYOR_BUILD_FOLDER\test\WebJobs.Script.Tests.Integration\bin\Release\Microsoft.Azure.WebJobs.Script.Tests.Integration.dll" -trait "E2E=EndToEndTimeoutTests"
$exitCode = CheckExitCode "EndToEndTimeoutTests tests" $exitCode

& $consoleRunnerx86Path "$env:APPVEYOR_BUILD_FOLDER\test\WebJobs.Script.Tests.Integration\bin\Release\Microsoft.Azure.WebJobs.Script.Tests.Integration.dll" -trait "E2E=CSharpEndToEndTests"
$exitCode = CheckExitCode "CSharpEndToEndTests tests" $exitCode

& $consoleRunnerx86Path "$env:APPVEYOR_BUILD_FOLDER\test\WebJobs.Script.Tests.Integration\bin\Release\Microsoft.Azure.WebJobs.Script.Tests.Integration.dll" -trait "E2E=NodeEndToEndTests"
$exitCode = CheckExitCode "NodeEndToEndTests tests" $exitCode

& $consoleRunnerx86Path "$env:APPVEYOR_BUILD_FOLDER\test\WebJobs.Script.Tests.Integration\bin\Release\Microsoft.Azure.WebJobs.Script.Tests.Integration.dll" -trait "E2E=BashEndToEndTests"
$exitCode = CheckExitCode "BashEndToEndTests tests" $exitCode

& $consoleRunnerx86Path "$env:APPVEYOR_BUILD_FOLDER\test\WebJobs.Script.Tests.Integration\bin\Release\Microsoft.Azure.WebJobs.Script.Tests.Integration.dll" -trait "E2E=DirectLoadEndToEndTests"
$exitCode = CheckExitCode "DirectLoadEndToEndTests tests" $exitCode

& $consoleRunnerx86Path "$env:APPVEYOR_BUILD_FOLDER\test\WebJobs.Script.Tests.Integration\bin\Release\Microsoft.Azure.WebJobs.Script.Tests.Integration.dll" -trait "E2E=FSharpEndToEndTests"
$exitCode = CheckExitCode "FSharpEndToEndTests tests" $exitCode

& $consoleRunnerx86Path "$env:APPVEYOR_BUILD_FOLDER\test\WebJobs.Script.Tests.Integration\bin\Release\Microsoft.Azure.WebJobs.Script.Tests.Integration.dll" -trait "E2E=PhpEndToEndTests"
$exitCode = CheckExitCode "PhpEndToEndTests tests" $exitCode

& $consoleRunnerx86Path "$env:APPVEYOR_BUILD_FOLDER\test\WebJobs.Script.Tests.Integration\bin\Release\Microsoft.Azure.WebJobs.Script.Tests.Integration.dll" -trait "E2E=PowerShellEndToEndTests"
$exitCode = CheckExitCode "PowerShellEndToEndTests tests" $exitCode

& $consoleRunnerx86Path "$env:APPVEYOR_BUILD_FOLDER\test\WebJobs.Script.Tests.Integration\bin\Release\Microsoft.Azure.WebJobs.Script.Tests.Integration.dll" -trait "E2E=PythonEndToEndTests"
$exitCode = CheckExitCode "PythonEndToEndTests tests" $exitCode

& $consoleRunnerx86Path "$env:APPVEYOR_BUILD_FOLDER\test\WebJobs.Script.Tests.Integration\bin\Release\Microsoft.Azure.WebJobs.Script.Tests.Integration.dll" -trait "E2E=RawAssemblyEndToEndTests"
$exitCode = CheckExitCode "RawAssemblyEndToEndTests tests" $exitCode

& $consoleRunnerx86Path "$env:APPVEYOR_BUILD_FOLDER\test\WebJobs.Script.Tests.Integration\bin\Release\Microsoft.Azure.WebJobs.Script.Tests.Integration.dll" -trait "E2E=SamplesEndToEndTests"
$exitCode = CheckExitCode "SamplesEndToEndTests tests" $exitCode

& $consoleRunnerPath "$env:APPVEYOR_BUILD_FOLDER\test\WebJobs.Script.Tests.Integration\bin\Release\Microsoft.Azure.WebJobs.Script.Tests.Integration.dll" -trait "E2E=NodeEndToEndTests"
$exitCode = CheckExitCode "NodeEndToEndTests tests (x64)" $exitCode

Write-Host "Completed test with with exit code $exitCode"

exit $exitCode