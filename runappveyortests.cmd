@echo off
set /a exitCode=0
vstest.console.exe "%APPVEYOR_BUILD_FOLDER%\test\WebJobs.Script.Tests\bin\Release\Microsoft.Azure.WebJobs.Script.Tests.dll" "%APPVEYOR_BUILD_FOLDER%\test\WebJobs.Script.Tests\bin\Release\xunit.runner.visualstudio.testadapter.dll" /logger:Appveyor /TestAdapterPath:"%APPVEYOR_BUILD_FOLDER%"
IF %ERRORLEVEL% NEQ 0 (set /a exitCode=1)
vstest.console.exe "%APPVEYOR_BUILD_FOLDER%\test\WebJobs.Script.Scaling.Tests\bin\Release\Microsoft.Azure.WebJobs.Script.Scaling.Tests.dll" "%APPVEYOR_BUILD_FOLDER%\test\WebJobs.Script.Tests\bin\Release\xunit.runner.visualstudio.testadapter.dll" /logger:Appveyor /TestAdapterPath:"%APPVEYOR_BUILD_FOLDER%"
IF %ERRORLEVEL% NEQ 0 (set /a exitCode=1)
call runNodeTests.cmd
IF %ERRORLEVEL% NEQ 0 (set /a exitCode=1)
vstest.console.exe "%APPVEYOR_BUILD_FOLDER%\test\WebJobs.Script.Tests.Integration\bin\Release\Microsoft.Azure.WebJobs.Script.Tests.Integration.dll" "%APPVEYOR_BUILD_FOLDER%\test\WebJobs.Script.Tests\bin\Release\xunit.runner.visualstudio.testadapter.dll" /logger:Appveyor /TestAdapterPath:"%APPVEYOR_BUILD_FOLDER%" /TestCaseFilter:"Category!=E2E"
IF %ERRORLEVEL% NEQ 0 (set /a exitCode=1)
vstest.console.exe "%APPVEYOR_BUILD_FOLDER%\test\WebJobs.Script.Tests.Integration\bin\Release\Microsoft.Azure.WebJobs.Script.Tests.Integration.dll" "%APPVEYOR_BUILD_FOLDER%\test\WebJobs.Script.Tests\bin\Release\xunit.runner.visualstudio.testadapter.dll" /logger:Appveyor /TestAdapterPath:"%APPVEYOR_BUILD_FOLDER%" /TestCaseFilter:"E2E=CSharpEndToEndTests"
IF %ERRORLEVEL% NEQ 0 (set /a exitCode=1)
vstest.console.exe "%APPVEYOR_BUILD_FOLDER%\test\WebJobs.Script.Tests.Integration\bin\Release\Microsoft.Azure.WebJobs.Script.Tests.Integration.dll" "%APPVEYOR_BUILD_FOLDER%\test\WebJobs.Script.Tests\bin\Release\xunit.runner.visualstudio.testadapter.dll" /logger:Appveyor /TestAdapterPath:"%APPVEYOR_BUILD_FOLDER%" /TestCaseFilter:"E2E=NodeEndToEndTests"
IF %ERRORLEVEL% NEQ 0 (set /a exitCode=1)
vstest.console.exe "%APPVEYOR_BUILD_FOLDER%\test\WebJobs.Script.Tests.Integration\bin\Release\Microsoft.Azure.WebJobs.Script.Tests.Integration.dll" "%APPVEYOR_BUILD_FOLDER%\test\WebJobs.Script.Tests\bin\Release\xunit.runner.visualstudio.testadapter.dll" /logger:Appveyor /TestAdapterPath:"%APPVEYOR_BUILD_FOLDER%" /TestCaseFilter:"E2E=BashEndToEndTests"
IF %ERRORLEVEL% NEQ 0 (set /a exitCode=1)
vstest.console.exe "%APPVEYOR_BUILD_FOLDER%\test\WebJobs.Script.Tests.Integration\bin\Release\Microsoft.Azure.WebJobs.Script.Tests.Integration.dll" "%APPVEYOR_BUILD_FOLDER%\test\WebJobs.Script.Tests\bin\Release\xunit.runner.visualstudio.testadapter.dll" /logger:Appveyor /TestAdapterPath:"%APPVEYOR_BUILD_FOLDER%" /TestCaseFilter:"E2E=DirectLoadEndToEndTests"
IF %ERRORLEVEL% NEQ 0 (set /a exitCode=1)
vstest.console.exe "%APPVEYOR_BUILD_FOLDER%\test\WebJobs.Script.Tests.Integration\bin\Release\Microsoft.Azure.WebJobs.Script.Tests.Integration.dll" "%APPVEYOR_BUILD_FOLDER%\test\WebJobs.Script.Tests\bin\Release\xunit.runner.visualstudio.testadapter.dll" /logger:Appveyor /TestAdapterPath:"%APPVEYOR_BUILD_FOLDER%" /TestCaseFilter:"E2E=FSharpEndToEndTests"
IF %ERRORLEVEL% NEQ 0 (set /a exitCode=1)
vstest.console.exe "%APPVEYOR_BUILD_FOLDER%\test\WebJobs.Script.Tests.Integration\bin\Release\Microsoft.Azure.WebJobs.Script.Tests.Integration.dll" "%APPVEYOR_BUILD_FOLDER%\test\WebJobs.Script.Tests\bin\Release\xunit.runner.visualstudio.testadapter.dll" /logger:Appveyor /TestAdapterPath:"%APPVEYOR_BUILD_FOLDER%" /TestCaseFilter:"E2E=PhpEndToEndTests"
IF %ERRORLEVEL% NEQ 0 (set /a exitCode=1)
vstest.console.exe "%APPVEYOR_BUILD_FOLDER%\test\WebJobs.Script.Tests.Integration\bin\Release\Microsoft.Azure.WebJobs.Script.Tests.Integration.dll" "%APPVEYOR_BUILD_FOLDER%\test\WebJobs.Script.Tests\bin\Release\xunit.runner.visualstudio.testadapter.dll" /logger:Appveyor /TestAdapterPath:"%APPVEYOR_BUILD_FOLDER%" /TestCaseFilter:"E2E=PowerShellEndToEndTests"
IF %ERRORLEVEL% NEQ 0 (set /a exitCode=1)
vstest.console.exe "%APPVEYOR_BUILD_FOLDER%\test\WebJobs.Script.Tests.Integration\bin\Release\Microsoft.Azure.WebJobs.Script.Tests.Integration.dll" "%APPVEYOR_BUILD_FOLDER%\test\WebJobs.Script.Tests\bin\Release\xunit.runner.visualstudio.testadapter.dll" /logger:Appveyor /TestAdapterPath:"%APPVEYOR_BUILD_FOLDER%" /TestCaseFilter:"E2E=PythonEndToEndTests"
IF %ERRORLEVEL% NEQ 0 (set /a exitCode=1)
vstest.console.exe "%APPVEYOR_BUILD_FOLDER%\test\WebJobs.Script.Tests.Integration\bin\Release\Microsoft.Azure.WebJobs.Script.Tests.Integration.dll" "%APPVEYOR_BUILD_FOLDER%\test\WebJobs.Script.Tests\bin\Release\xunit.runner.visualstudio.testadapter.dll" /logger:Appveyor /TestAdapterPath:"%APPVEYOR_BUILD_FOLDER%" /TestCaseFilter:"E2E=RawAssemblyEndToEndTests"
IF %ERRORLEVEL% NEQ 0 (set /a exitCode=1)
vstest.console.exe "%APPVEYOR_BUILD_FOLDER%\test\WebJobs.Script.Tests.Integration\bin\Release\Microsoft.Azure.WebJobs.Script.Tests.Integration.dll" "%APPVEYOR_BUILD_FOLDER%\test\WebJobs.Script.Tests\bin\Release\xunit.runner.visualstudio.testadapter.dll" /logger:Appveyor /TestAdapterPath:"%APPVEYOR_BUILD_FOLDER%" /TestCaseFilter:"E2E=SamplesEndToEndTests"
IF %ERRORLEVEL% NEQ 0 (set /a exitCode=1)

EXIT /B %exitCode%