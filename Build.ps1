if (Test-Path "env:ProgramFiles(x86)")
{
    $ProgramFiles = "${env:ProgramFiles(x86)}"
}
else
{
    $ProgramFiles = "$env:ProgramFiles"
}

& "$ProgramFiles\MSBuild\12.0\Bin\MSBuild.exe" WebJobs.proj $Args
