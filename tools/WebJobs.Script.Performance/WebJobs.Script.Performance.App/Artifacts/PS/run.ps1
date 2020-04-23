param (
	[string]$toolNupkgUrl = "",
    [string]$testId = "",
    [string]$runtimeUrl = ""
)

# for tests
#$toolNupkgUrl = "https://functionsperfst.blob.core.windows.net/test/WebJobs.Script.Performance.App.1.0.0.nupkg"
#$testId = "Java-Ping"
#$runtimeUrl = "https://ci.appveyor.com/api/buildjobs/ax3jch5m0d57hdkm/artifacts/Functions.Private.2.0.12165.win-x32.inproc.zip"

function ZipContent([string] $sourceDirectory, [string] $target)
{
  Write-Host $sourceDirectory
  Write-Host $target
  
  if (Test-Path $target) {
    Remove-Item $target
  }
  Add-Type -assembly "system.io.compression.filesystem"
  [IO.Compression.ZipFile]::CreateFromDirectory($sourceDirectory, $target)
}

function ConnectToAzure()
{
	$passwd = ConvertTo-SecureString $settings.AzureWebJobsTargetSiteClientSecret -AsPlainText -Force
	# $settings.password 'service principal name/id'
	$pscredential = New-Object System.Management.Automation.PSCredential($settings.AzureWebJobsTargetSiteApplicationId, $passwd)
	
    # https://azure.microsoft.com/en-us/blog/how-to-migrate-from-azurerm-to-az-in-azure-powershell/
	Connect-AzAccount -ServicePrincipal -Credential $pscredential -TenantId $settings.AzureWebJobsTargetSiteTenantId
}

$currentTime = "$((Get-Date).ToUniversalTime().ToString('yyyy-MM-dd_HH_mm_ss'))"
Start-Transcript -path "C:\Tools\output\run\run$currentTime.log" -append

Write-Output "toolNupkgUrl: $toolNupkgUrl"
Write-Output "testId: $testId"
Write-Output "runtimeUrl: $runtimeUrl"

# check input vars
if ([string]::IsNullOrEmpty($toolNupkgUrl)) {
	Write-Host "toolNupkgUrl is not defined"
	exit
}

if ([string]::IsNullOrEmpty($runtimeUrl)) {
	Write-Host "runtimeUrl is not defined"
	exit
}

# Get latest scenarios repo
$scenariosDir = "C:\git\azure-functions-performance-scenarios"
Write-Output "Getting latest 'azure-functions-performance-scenarios'"
Push-Location $scenariosDir
& git pull

Pop-Location

# Updating the tool
$tempFolderName = [System.IO.Path]::GetFileNameWithoutExtension([System.IO.Path]::GetTempFileName())
$tempFolder = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath() ,$tempFolderName)
[System.IO.Directory]::CreateDirectory($tempFolder)
$binPath = "$tempFolder\.store\webjobs.script.performance.app\1.0.0\webjobs.script.performance.app\1.0.0\tools\netcoreapp2.1\any\"
$filename = $toolNupkgUrl.Substring($toolNupkgUrl.LastIndexOf("%2F") + 3)
$nupkgPath = "$tempFolder\$filename"
Write-Host "Downloading '$toolNupkgUrl' to '$nupkgPath'"
Invoke-WebRequest -Uri $toolNupkgUrl -OutFile $nupkgPath
& dotnet tool install "WebJobs.Script.Performance.App" --add-source "$tempFolder" --tool-path $tempFolder
Copy-Item -Path "C:\Tools\ps\local.settings.json" -Destination $binPath -Force

$settings = Get-Content "$binPath\local.settings.json" | Out-String | ConvertFrom-Json
ConnectToAzure 

Write-Host "Scanning $scenariosDir'.."
Get-ChildItem $scenariosDir -Directory |
Foreach-Object {
    $fullName = $_.FullName
    $name = $_.Name

    if (($name -eq "") -or (($name -eq $testId) -or ($testId -eq ""))) {
        Write-Output "Processing folder: $fullName"
        $configPath = $fullName + "\config.json"
        if (Test-Path $configPath) {
            $config = Get-Content $configPath | Out-String | ConvertFrom-Json
            Write-Output "Config found: $config"
            $contentDir = "$fullName\Content\"
            if ((-not $config.isScript) -and ($config.runtime -eq "dotnet")) {
                # Getting csproj
                $csprojFile =  Get-ChildItem $contentDir -Filter *.csproj | Select-Object -First 1                
                $csprojFile = $_.FullName + "\Content\$csprojFile"
                Write-Output "Building .NET Core project: $csprojFile"
                & dotnet build -c Release $csprojFile
                $zipPath = "$fullName\$_.zip"
                $contentDir = "$contentDir\bin\Release\netcoreapp2.1"
            } elseif ($config.runtime -eq "java") {
                Write-Output "Building Java project.."
                Copy-Item -Path $contentDir -Destination $tempFolder -Recurse
                & mvn clean package "-Dorg.slf4j.simpleLogger.log.org.apache.maven.cli.transfer.Slf4jMavenTransferListener=warn" -f "$tempFolder\Content" -B
                Copy-Item -Path $tempFolder\Content\target\azure-functions\java-test\test-1.0-SNAPSHOT.jar -Destination "$fullName\Content" -Force
            }
            
            $zipPath = "$tempFolder\$_.zip"
            $jmxPath = "$fullName\test.jmx"
            Write-Output "Generating zip: $zipPath"            
            ZipContent $contentDir $zipPath

            # getting storage account
            $storageAccount = Get-AzStorageAccount -StorageAccountName $settings.PerfStorageAccountName `
                -ResourceGroupName $settings.AzureWebJobsTargetSiteResourceGroup
            $ctx = $storageAccount.Context

            # upload run-from-zip
            $blobPathZip = "$currentTime/$name.zip"
            Set-AzStorageblobcontent -File $zipPath `
                -Container "artifacts" `
                -Blob $blobPathZip `
                -Context $ctx
            
            # upload jmx
            $blobPathJmx = "$currentTime/$name.jmx"
            Set-AzStorageblobcontent -File $jmxPath `
                -Container "artifacts" `
                -Blob $blobPathJmx `
                -Context $ctx
            
            $blobPathZip = "https://" + $settings.PerfStorageAccountName + ".blob.core.windows.net/artifacts/$blobPathZip" + "?" + $settings.SASQueryString            
            $blobPathJmx = "https://" + $settings.PerfStorageAccountName + ".blob.core.windows.net/artifacts/$blobPathJmx" + "?" + $settings.SASQueryString            
            $toolArgs = "-u '$runtimeUrl' -r '" + $config.runtime + "' -z '$blobPathZip' -j '$blobPathJmx' -d '" + $config.description + "'"

            Push-Location $binPath
            Write-Output "Running tool: dotnet $binPath\WebJobs.Script.Performance.App.dll $toolArgs"
            $output = Invoke-Expression "dotnet $binPath\WebJobs.Script.Performance.App.dll $toolArgs"
            Write-Output $output
            Pop-Location

        } else {
            Write-Output "No config found: $configPath" 
        }
    }
}

Write-Output "Cleaning $tempFolder"
Remove-Item -Recurse -Force $tempFolder -ErrorAction SilentlyContinue
Write-Output "Cleaning C:\Windows\Temp"
Remove-Item -Recurse -Force C:\Windows\Temp -ErrorAction SilentlyContinue
Stop-Transcript