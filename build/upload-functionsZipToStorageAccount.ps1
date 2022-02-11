param (
    [Parameter(Mandatory=$true)]
    [ValidateNotNullOrEmpty()]
    [System.String]
    $StorageAccountName,

    [Parameter(Mandatory=$true)]
    [ValidateNotNullOrEmpty()]
    [System.String]
    $StorageAccountKey,

    [Parameter(Mandatory=$true)]
    [ValidateNotNullOrEmpty()]
    [System.String]
    $SourcePath,

    [Parameter(Mandatory=$false)]
    [ValidateNotNullOrEmpty()]
    [System.String]
    $FunctionsRuntimeVersion = '4'
)

function WriteLog
{
    param (
        [Parameter(Mandatory=$true)]
        [ValidateNotNullOrEmpty()]
        [System.String]
        $Message,

        [Switch]
        $Throw
    )

    $Message = (Get-Date -Format G)  + " -- $Message"

    if ($Throw)
    {
        throw $Message
    }

    Write-Host $Message
}

function GetContentType
{
    param
    (
        [Parameter(Mandatory=$true)]
        [ValidateNotNullOrEmpty()]
        [System.String]
        $FilePath
    )

    $fileExtension =  [System.IO.Path]::GetExtension($FilePath)

    switch ($fileExtension)
    {
        ".txt" { "text/plain" }
        ".json" { "application/json" }
        default { "application/octet-stream" }
    }
}

function GetFullPath
{
    param
    (
        [Parameter(Mandatory=$true)]
        [ValidateNotNullOrEmpty()]
        [System.String]
        $FilePath
    )

    $FilePath = (Get-Item $FilePath -ErrorAction SilentlyContinue).FullName

    if (-not $FilePath)
    {
        WriteLog -Message "Required file '$FilePath' does not exist." -Throw
    }

    return $FilePath
}

function GetFunctionsBuildVersion
{
    $filePath = GetFullPath -FilePath "$SourcePath/Functions.*.zip"
    $fileName = Split-Path -Path $filePath -Leaf

    $pattern = 'Functions.(.+).zip'    
    $version = $fileName -replace $pattern,'$1'

    if (-not $version)
    {
        WriteLog -Message "Failed to parse version string from '$fileName'." -Throw
    }

    return $version
}

WriteLog -Message "Script started."

return

if (-not (Test-Path $SourcePath))
{
    throw "SourcePath '$SourcePath' does not exist."
}

$CONTAINER_NAME = "functionsbuilds"

# This is the list of files to upload
$filesToUpload = @(
    GetFullPath -FilePath "$SourcePath/Functions.*.zip"
)

# Create the version.txt file
$version = GetFunctionsBuildVersion
$versionTxtFilePath = "$SourcePath/version.txt"
Set-Content -Path $versionTxtFilePath -Value $version -Force

$filesToUpload += $versionTxtFilePath

if (-not (Get-command New-AzStorageContext -ea SilentlyContinue))
{
    WriteLog "Installing Az.Storage."
    Install-Module Az.Storage -Force -Verbose -AllowClobber -Scope CurrentUser
}

$context = $null
try
{
    WriteLog "Connecting to storage account..."
    $context = New-AzStorageContext -StorageAccountName $StorageAccountName -StorageAccountKey $StorageAccountKey -ErrorAction Stop
}
catch
{
    $message = "Failed to authenticate with Azure. Please verify the StorageAccountName and StorageAccountKey. Exception information: $_"
    WriteLog -Message $message -Throw
}

# These are the destination paths in the storage account
# "https://<storageAccountName>.blob.core.windows.net/$CONTAINER_NAME/$FunctionsRuntimeVersion/latest/PackageInfo.json"
# "https://<storageAccountName>.blob.core.windows.net/$CONTAINER_NAME/$FunctionsRuntimeVersion/$version/Functions.<version>.zip"
$latestDestinationPath = "$FunctionsRuntimeVersion/latest"
$versionDestinationPath = "$FunctionsRuntimeVersion/$($version)"

# Delete the files in the latest folder if it is not empty
$filesToDelete = @(Get-AzStorageBlob -Container $CONTAINER_NAME -Context $context -ErrorAction SilentlyContinue | Where-Object {$_.Name -like "*$latestDestinationPath*" })
if ($filesToDelete.Count -gt 0)
{
    WriteLog -Message "Deleting files in the latest folder...."
    $filesToDelete | ForEach-Object {
        Remove-AzStorageBlob -Container $CONTAINER_NAME  -Context $context -Blob $_.Name -Force -ErrorAction SilentlyContinue | Out-Null
    }
}

try
{
    foreach ($path in @($latestDestinationPath, $versionDestinationPath))
    {
        foreach ($file in $filesToUpload)
        {
            $fileName = Split-Path $file -Leaf
            $destinationPath = Join-Path $path $fileName
    
            $contentType = GetContentType -FilePath $file
    
            if ($destinationPath -like "*latest*")
            {
                # Remove the version from the path for latest
                $destinationPath = $destinationPath.Replace("." + $version, "")
            }
    
            try
            {
                WriteLog -Message "Uploading '$fileName' to '$destinationPath'."
    
                Set-AzStorageBlobContent -File $file `
                                         -Container $CONTAINER_NAME `
                                         -Blob $destinationPath `
                                         -Context $context `
                                         -StandardBlobTier Hot `
                                         -ErrorAction Stop `
                                         -Properties  @{"ContentType" = $contentType} `
                                         -Force | Out-Null
            }
            catch
            {
                WriteLog -Message "Failed to upload file '$file' to storage account. Exception information: $_" -Throw
            }
        }
    }
}
finally
{
    if (Test-Path $versionTxtFilePath)
    {
        WriteLog -Message "Cleanup: remove '$versionTxtFilePath'"
        Remove-Item $versionTxtFilePath -Force -ErrorAction SilentlyContinue
    }
}

WriteLog -Message "Script completed."
