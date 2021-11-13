#!/bin/bash

set -e

msbuild_root="$(pwd)/_work/msbuild"
configuration="Release"

if [[ -d "_work" ]]; then rm -rf _work; fi

mkdir -p "$(dirname $msbuild_root)"
pushd "$(dirname $msbuild_root)"
git clone https://github.com/samhowes/msbuild --depth 1 --branch vs16.9 --single-branch
git checkout -b rules_msbuild/vs16.9
