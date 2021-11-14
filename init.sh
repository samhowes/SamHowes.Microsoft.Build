#!/bin/bash

set -e

msbuild_root="$(pwd)/_work/msbuild"

if [[ -d "_work" ]]; then rm -rf _work; fi

mkdir -p "$(dirname $msbuild_root)"
pushd "$(dirname $msbuild_root)"
git clone https://github.com/samhowes/msbuild --depth 1 --branch vs17.0 --single-branch
