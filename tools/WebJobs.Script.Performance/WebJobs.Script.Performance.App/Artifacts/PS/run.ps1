param (
	[string]$toolNupkgUrl = "",
    [string]$toolArgs = ""
)

# for tests
#$toolNupkgUrl = "https://functionsperfst.blob.core.windows.net/test/WebJobs.Script.Performance.App.1.0.0.nupkg"
#$toolArgs = "-t win-csharp-ping.jmx -r https://ci.appveyor.com/api/buildjobs/ax3jch5m0d57hdkm/artifacts/Functions.Private.2.0.12165.win-x32.inproc.zip"

$currentTime = "$((Get-Date).ToUniversalTime().ToString('yyyy-MM-dd HH_mm_ss'))"
Start-Transcript -path "C:\Tools\output\run\run$currentTime.log" -append

Write-Output "Tool: $toolNupkgUrl"
Write-Output "Args: $toolArgs"

if ([string]::IsNullOrEmpty($toolNupkgUrl)) {
	Write-Host "Tool url is not defined"
	exit
}

if ([string]::IsNullOrEmpty($toolArgs)) {
	Write-Host "Arguments for the tool is not defined"
	exit
}


$tempFolderName = [System.IO.Path]::GetFileNameWithoutExtension([System.IO.Path]::GetTempFileName())
$tempFolder = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath() ,$tempFolderName)
$binPath = "$tempFolder\.store\webjobs.script.performance.app\1.0.0\webjobs.script.performance.app\1.0.0\tools\netcoreapp2.1\any\"
[System.IO.Directory]::CreateDirectory($tempFolder)
$filename = $toolNupkgUrl.Substring($toolNupkgUrl.LastIndexOf("/") + 1)
$nupkgPath = "$tempFolder\$filename"
   
Write-Host "Downloading '$toolNupkgUrl' to '$nupkgPath'"
Invoke-WebRequest -Uri $toolNupkgUrl -OutFile $nupkgPath

& dotnet tool install "WebJobs.Script.Performance.App" --add-source "$tempFolder" --tool-path $tempFolder

Copy-Item -Path "C:\Tools\ps\local.settings.json" -Destination $binPath -Force

Push-Location "$binPath\Artifacts\PS"
Invoke-Expression "$binPath\Artifacts\PS\build-jar.ps1"
Pop-Location

Push-Location $binPath
Write-Output "Running tool: dotnet $binPath\WebJobs.Script.Performance.App.dll $toolArgs"
Invoke-Expression "dotnet $binPath\WebJobs.Script.Performance.App.dll $toolArgs"
Pop-Location

Remove-Item -Recurse -Force $tempFolder -ErrorAction SilentlyContinue

Stop-Transcript