#!/bin/bash

set -e

pushd _work/msbuild
git checkout vs16.9
git reset --hard
git branch -D rules_msbuild/vs16.9 || echo ""
git checkout -b rules_msbuild/vs16.9
popd


pushd Converter
dotnet run
popd

pushd _work/msbuild
proj="$(pwd)/src/Build/Microsoft.Build.csproj"

./build.sh -pack -projects "$proj" -configuration Release

rm -rf ~/.nuget/packages/samhowes.microsoft.build 
