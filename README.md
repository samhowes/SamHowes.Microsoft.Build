# SamHowes.Microsoft.Build

The code in this repository modifies the source code of github.com/dotnet/msbuild, then re-packages it in a NuGet Package called `SamHowes.Microsoft.Build`.

This is useful for github.com/samhowes/rules_msbuild which needs deeper access to BuildManager apis than is available by default.

Specifically: many apis are made `public` instead of `internal`, and some code is customized to allow for replacing internal services.

# Usage
```
# make sure these repositories exist side-by-side
git clone github.com/dotnet/msbuild
git clone github.com/samhowes/SamHowes.Microsoft.Build

pushd SamHowes.Microsoft.Build
./build.sh
popd

pushd msbuild
ls artifacts/packages/Release/Shipping/SamHowes.Microsoft.Build.16.9.0.nupkg

# the nupkg can now be uploaded to a gihub release for consumption.

```
