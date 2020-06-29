@ECHO Off
REM call dotnet --info
call dotnet restore WebJobs.Script.sln
call dotnet build WebJobs.Script.sln
REM call dotnet test WebJobs.Script.sln --no-build