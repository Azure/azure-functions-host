$currentDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$buildOutput = Join-Path $currentDir "buildoutput"
$dotnetx86 = Join-Path ${env:ProgramFiles(x86)} dotnet\dotnet.exe
$success = $true

# dotnetx86 test .\test\WebJobs.Script.Scaling.Tests\ -v q --no-build -p:ParallelizeTestCollections=false
# dotnetx86 test .\test\WebJobs.Script.Tests\ -v q --no-build -p:ParallelizeTestCollections=false
# see results in app insights AntaresDemo 'functionse2eai'

& $dotnetx86 --version

& $dotnetx86 --info
      
& $dotnetx86 test .\test\WebJobs.Script.Tests -v q --no-build

$success = $success -and $?

& $dotnetx86 test .\test\WebJobs.Script.Scaling.Tests -v q --no-build

$success = $success -and $?

& $dotnetx86 test .\test\WebJobs.Script.Tests.Integration\ -v q --no-build

$success = $success -and $?

if (-not $success) { exit 1 }