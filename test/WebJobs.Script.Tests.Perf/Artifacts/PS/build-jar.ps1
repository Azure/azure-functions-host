$tempFolderName = [System.IO.Path]::GetFileNameWithoutExtension([System.IO.Path]::GetTempFileName())
$tempFolder = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath() ,$tempFolderName)
[System.IO.Directory]::CreateDirectory($tempFolder)

Write-Host "tempFolder:$tempFolder"

Copy-Item -Path ".\..\Java\*" -Destination $tempFolder -Recurse

& mvn clean package --file "$tempFolder"

Copy-Item -Path $tempFolder\target\azure-functions\java-test\test-1.0-SNAPSHOT.jar -Destination ".\..\..\Functions\wwwroot" -Force