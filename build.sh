#!/bin/bash

set -e

pushd Converter
dotnet run
popd

msbuild_root="$(realpath "$(pwd)/../msbuild")"
pushd "$msbuild_root"
proj="$msbuild_root/src/Build/Microsoft.Build.csproj"

./build.sh -pack -projects "$proj" 

rm -rf ~/.nuget/packages/samhowes.microsoft.build 
