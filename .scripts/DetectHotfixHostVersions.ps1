$API_BASE_URL = "https://vsrm.dev.azure.com/"
# The below variables are passed via ADO
$REPO_NAME = "$(repoName)"
$PROJECT_NAME = "$(projectName)"
$RELEASE_DEFINITION_ID = "$(releaseDefinitionId)"
$TEST_STEP_NAME = "$(testStepName)"

$RELEASE_LIST_GET_URL = $API_BASE_URL + "/" + $REPO_NAME + "/" + $PROJECT_NAME + "/_apis/release/release?definitionId=" + $RELEASE_DEFINITION_ID + "&api-version=7.0"

$response = Invoke-RestMethod $RELEASE_LIST_GET_URL -Method 'GET' -Headers $headers
$response | ConvertTo-Json
$arrayOfReleases = @()
foreach ($release in $response.value)
{
    $RELEASE_INFORMATION_GET_URL = $API_BASE_URL + "/" + $REPO_NAME + "/" + $PROJECT_NAME + "/_apis/release/releases/" + [int]$release.id + '?api-version=7.0'
    $releaseResponse = Invoke-RestMethod $RELEASE_INFORMATION_GET_URL -Method 'GET' -Headers $headers
    $releaseResponse | ConvertTo-Json
    $OGFsForRelease = $releaseResponse.environments | where { $_.name -eq $TEST_STEP_NAME }
    if (($OGFsForRelease.status -eq "inProgress") -and ($releaseResponse.status -ne "abandoned"))
    {
        $arrayOfReleases += $releaseResponse.artifacts.definitionReference.version.name
    }
}
Write-Host "Releases in-progress: " $arrayOfReleases