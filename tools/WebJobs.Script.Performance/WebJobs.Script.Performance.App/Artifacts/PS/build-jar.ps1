$tempFolderName = [System.IO.Path]::GetFileNameWithoutExtension([System.IO.Path]::GetTempFileName())
$tempFolder = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath() ,$tempFolderName)
[System.IO.Directory]::CreateDirectory($tempFolder)

Write-Host "tempFolder:$tempFolder"

Copy-Item -Path ".\..\Java\*" -Destination $tempFolder -Recurse

& mvn clean package "-Dorg.slf4j.simpleLogger.log.org.apache.maven.cli.transfer.Slf4jMavenTransferListener=warn" -f "$tempFolder" -B

Copy-Item -Path $tempFolder\target\azure-functions\java-test\test-1.0-SNAPSHOT.jar -Destination ".\..\..\Functions\wwwroot" -Force