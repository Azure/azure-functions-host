# The below variables are passed via ADO
git config --global user.email "azfuncgh@github.com"
git config --global user.name "azfuncgh"
$listOfTags = "$(commaSeparatedListOfTags)".split(",")
$commitID = "$(commitId)"
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
    git push
}