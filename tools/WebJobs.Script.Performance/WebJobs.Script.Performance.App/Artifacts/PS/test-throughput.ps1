# Install-Module -Name AzureRM
# Add-Type -AssemblyName "System.Web"
# https://www.youtube.com/watch?v=ZiGGBeA97lA - JMeter dashboard htmls
# https://docs.microsoft.com/en-us/azure/azure-resource-manager/resource-group-create-service-principal-portal - give you application rights to acess a VM

param (
    [string]$jmx = "win-csharp-ping.jmx",
    [string]$desc = "Empty",
    [string]$runtime = "Empty"
)

Add-Type -AssemblyName "System.Web"

function Upload-FileToAzureStorageContainer {
    [cmdletbinding()]
    param(
        $StorageAccountName,
        $StorageAccountKey,
        $ContainerName,
        $sourceFileRootDirectory,
        $folderName,
        $Force
    )

    $ctx = New-AzureStorageContext -StorageAccountName $StorageAccountName -StorageAccountKey $StorageAccountKey
    $container = Get-AzureStorageContainer -Name $ContainerName -Context $ctx

    $container.CloudBlobContainer.Uri.AbsoluteUri
    if ($container) {
        $filesToUpload = Get-ChildItem $sourceFileRootDirectory -Recurse -File       

        foreach ($x in $filesToUpload) {
            $contentType = [System.Web.MimeMapping]::GetMimeMapping($x)
            $blobProperties = @{"ContentType" = $contentType}
            $targetPath = ("$folderName/" + $x.fullname.Substring($sourceFileRootDirectory.Length + 1)).Replace("\", "/")

            Write-Verbose "Uploading $("\" + $x.fullname.Substring($sourceFileRootDirectory.Length + 1)) to $($container.CloudBlobContainer.Uri.AbsoluteUri + "/" + $targetPath)"
            Set-AzureStorageBlobContent -File $x.fullname -Container $container.Name -Blob $targetPath -Properties $blobProperties -Context $ctx -Force:$Force | Out-Null
        }
    }
}

$folderName = "$((Get-Date).ToUniversalTime().ToString('yyyy-MM-dd HH_mm_ss'))"
$outputCSVPath = "C:\Tools\output\csv\$folderName"
$outputHTMLPath = "C:\Tools\output\html\$folderName"
$jmeter = "C:\Program Files\Java\jre1.8.0_181\bin\java"

New-Item -ItemType Directory -Path $outputCSVPath
New-Item -ItemType Directory -Path $outputHTMLPath

Start-Transcript -path $outputCSVPath\test.log -append

Write-Output "Runtime: $runtime"
Write-Output "jmx: $jmx"

$tempFolderName = [System.IO.Path]::GetFileNameWithoutExtension([System.IO.Path]::GetTempFileName())
$tempJmxPath = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath() ,$tempFolderName)
[System.IO.Directory]::CreateDirectory($tempJmxPath)
$tempJmxPath = "$tempJmxPath\test.jmx"
Write-Host "Downloading '$jmx' to '$tempJmxPath'"
Invoke-WebRequest -Uri $jmx -OutFile $tempJmxPath

Write-Host "$jmeter -jar C:\Tools\apache-jmeter-5.0\bin\ApacheJMeter.jar -n -t $tempJmxPath -l '$outputCSVPath\logs.csv'"
& $jmeter -jar C:\Tools\apache-jmeter-5.0\bin\ApacheJMeter.jar -n -t $tempJmxPath -l "$outputCSVPath\logs.csv"
Write-Host "$jmeter -jar C:\Tools\apache-jmeter-5.0\bin\ApacheJMeter.jar -g '$outputCSVPath\logs.csv' -o $outputHTMLPath"
& $jmeter -jar C:\Tools\apache-jmeter-5.0\bin\ApacheJMeter.jar -g "$outputCSVPath\logs.csv" -o $outputHTMLPath

$matches = Select-String -Pattern "#statisticsTable" -Path "$outputHTMLPath\content\js\dashboard.js"
$ret = [Regex]::Matches($matches[0], "(?<=\[""Total"", ).+?(?=], \""isController"")")
Set-Content -Path "$outputHTMLPath\summary.txt" -Value "$desc, $runtime, $ret"

$StorageAccountName = $env:DashboardStorageName
$StorageAccountKey = $env:DashboardKey
$ContainerName = "dashboard"

Upload-FileToAzureStorageContainer -StorageAccountName $StorageAccountName -StorageAccountKey $StorageAccountKey -ContainerName $ContainerName -sourceFileRootDirectory $outputHTMLPath $folderName -Verbose
Stop-Transcript
Upload-FileToAzureStorageContainer -StorageAccountName $StorageAccountName -StorageAccountKey $StorageAccountKey -ContainerName $ContainerName -sourceFileRootDirectory $outputCSVPath $folderName -Verbose


