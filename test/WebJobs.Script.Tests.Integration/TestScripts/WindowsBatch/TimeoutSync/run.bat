@echo off

SET /p input=<%inputData%
echo %input%

REM Sleep for 30 seconds. The test should timeout after 3 but this allows the process to exit gracefully.
REM Note that this won't work in Azure since it appears ping isn't allowed

ping localhost -n 11 > nul