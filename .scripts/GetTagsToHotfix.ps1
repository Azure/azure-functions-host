$listOfTags = "4.19.0,4.17.4".split(",")
$commitID = "23ae5d28a8431e51d99cd15c1cbff2ce9ad408d1"
foreach ($tag in $listOfTags)
{
    git checkout dev
    git pull
    git checkout "v$tag"
    git cherry-pick $commitID
    if (!$tag.StartsWith("release"))
    {
        $version = $tag.split('.')
        $patchVersion = [int]$version[1] + 1
        $version[1] = [str]$patchVersion
        $tag = $version.join('.')
        git tag $tag
    }
    # git push
}