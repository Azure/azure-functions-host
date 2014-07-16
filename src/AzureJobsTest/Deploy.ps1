if ($Args.Length -eq 0)
{
    Write-Host "Usage: Deploy.ps1 <scm_uri>"
    exit
}

$ScmUri = $Args[0]

if (Test-Path "env:ProgramFiles(x86)")
{
    $ProgramFiles = "${env:ProgramFiles(x86)}"
}
else
{
    $ProgramFiles = "$env:ProgramFiles"
}

$CurlExe = "$ProgramFiles\git\bin\curl.exe"

if (!(Test-Path $CurlExe))
{
    Write-Host ($CurlExe + "does not exist")
    exit
}

& "$ProgramFiles\MSBuild\12.0\Bin\MSBuild.exe" AzureJobsTest.proj
$ZipFile = "bin\AzureJobsTest.zip"
$CurlArguments = '-k -v -T "' + $ZipFile + '" "' + $ScmUri + '/zip"'

Write-Host ($CurlExe + " " + '-k -v -T "' + $ZipFile + '" "' + $ScmUri + '/zip"')
& $CurlExe -k -v -T "$ZipFile" "$ScmUri/zip"

Write-Host
Write-Host "Add the following app setting: WEBSITE_PRIVATE_EXTENSIONS = 1"
Write-Host
