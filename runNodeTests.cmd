@echo off

echo Locating MSBuild.exe..
for /f "tokens=*" %%i in ('vswhere -latest -requires Microsoft.Component.MsBuild -find MSbuild\**\Bin\MSBuild.exe') do (
    echo Found MSBuild.exe at: %%i
    set msbuildpath="%%i"
)

set _config=Debug
if defined CONFIGURATION (
    if not [%CONFIGURATION%]=="" (
    set _config=%CONFIGURATION%
    )
)
if not "%~1"=="" (
    set _config=%~1
)
echo _config is "%_config%"

set _junitReportPath=%~dp0test-results.xml
if defined MOCHA_FILE (
    set _junitReportPath=%MOCHA_FILE%
)
echo Running Node tests. Test results will be written to: %_junitReportPath%

%msbuildpath% Webjobs.Script.proj /t:MochaTest /p:Configuration="%_config%"