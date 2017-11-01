@ECHO Off
call dotnet --version
call dotnet restore WebJobs.Script.sln
call dotnet build WebJobs.Script.sln
