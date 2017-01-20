param($installPath, $toolsPath, $package, $project)

function MarkDirectoryAsCopyToOutputRecursive($item)
{
    $item.ProjectItems | ForEach-Object { MarkFileASCopyToOutputDirectory($_) }
}

function MarkFileASCopyToOutputDirectory($item)
{
    Try
    {
        Write-Host Try set $item.Name
        $item.Properties.Item("CopyToOutputDirectory").Value = 2
    }
    Catch
    {
        Write-Host RecurseOn $item.Name
        MarkDirectoryAsCopyToOutputRecursive($item)
    }
}

# nuget packages are a zip
# objects in the 'content' directory will be added to the user project root
# we have content/Content/Scripts/functions.js -> add Content folder to root of user project
# recursively step through Content folder of user project marking objects as CopyAlways
MarkDirectoryAsCopyToOutputRecursive($project.ProjectItems.Item("azurefunctions"))