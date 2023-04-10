git config --global user.email "azfuncgh@github.com"
git config --global user.name "azfuncgh"
# The below variables are passed via ADO
$githubToken = "$(GithubPATazfuncgh)"
$listOfTags = "$(commaSeparatedListOfTags)".split(",")
$commitID = "$(commitId)"
git clone https://$githubToken@github.com/Azure/azure-functions-host
git checkout dev
git pull
foreach ($tag in $listOfTags)
{
    git checkout "v$tag"
    git cherry-pick $commitID
    if (!$(tag).StartsWith("release"))
    {
        $version = $tag.split('.')
        $patchVersion = [int]$version[1] + 1
        $version[1] = [str]$patchVersion
        $tag = $version.join('.')
        git tag $tag
    }
    git add .
    git commit -m "Hotfix release for $tag"
    try {
        git push https://$githubToken@github.com/Azure/azure-functions-host.git
        if (-not $?) {
            throw "Error with git push tag!"
        }
    }
    catch {
        Write-Host $_.ScriptStackTrace
         throw "Error occurred while trying to do the final push. Exiting"
    }
}