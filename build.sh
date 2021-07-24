#!/bin/bash

set -e


msbuild_root="$(realpath "$(pwd)/../msbuild")"
pushd "$msbuild_root"
git reset --hard
popd

if [[ -n "${1:-}" ]]; then
    exit
fi

pushd Converter
dotnet run
popd

pushd "$msbuild_root"
proj="$msbuild_root/src/Build/Microsoft.Build.csproj"

./build.sh -pack -projects "$proj" 

rm -rf ~/.nuget/packages/samhowes.microsoft.build 
