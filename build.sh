#!/bin/bash

set -e

pushd Converter
dotnet run
popd

pushd _work/msbuild
proj="$(pwd)/src/Build/Microsoft.Build.csproj"

./build.sh -pack -projects "$proj" -configuration Release

rm -rf ~/.nuget/packages/samhowes.microsoft.build 
