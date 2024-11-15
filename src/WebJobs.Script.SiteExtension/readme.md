# Site Extension

This project is responsible for building the artifacts we ship to antares as a site extension.

## Usage

Like any MSBuild project, this can be restored, build, and published separately or together.

``` shell
# Together
dotnet publish -c {config}

# Separately
dotnet restore -p:PublishReadyToRun=true # needs to be set to true (fixed in .net9 SDK)
dotnet build -c {config} --no-restore
dotnet publish -c {config} --no-build
```

By default the outputs will not be zipped. To the zip the final outputs, add `-p:ZipAfterPublish=true` to the `publish` command.


## Outputs

The output site extension can be found at `{repo_root}/out/pub/WebJobs.Script.SiteExtension/{config}_win`. When using `-p:ZipAfterPublish=true`, the zipped package is found at `{repo_root}/out/pkg/{config}`
