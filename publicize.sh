#!/bin/bash

msbuild_root="$(realpath "$(pwd)/../msbuild")"
src_root="$msbuild_root/src"
framework="$src_root/Build"
shared="$src_root/Shared"


if [[ "$1" == "test" ]]; then 
    folders=( "$(pwd)" )
    proj="$(pwd)/test.proj"
    original_cs="$(cat test.cs)"
    original_proj="$(cat "$proj")"
    
else
    folders=( "$shared" "$framework" )
    proj="$src_root/Build/Microsoft.Build.csproj"
fi

for r in "${folders[@]}"
do
    # find "$r" -name "*.cs" -type f #-print0 | xargs -n 1 -0 sed -E 's/^([[:space:]]+)internal/\1public/g'
    files=$(find "$r" -name "*.cs" -type f)
    echo "$files" | xargs -n 1 sed -i '' -E 's/^([[:space:]]+((static|sealed|abstract|protected)[[:space:]])?)internal/\1public/g'
    echo "$files" | xargs -n 1 sed -i '' -E 's/(protected (public))|(public (set))/\2\4/g'
    echo "$files" | xargs -n 1 sed -i '' -E 's/(System.Reflection.)?BindingFlags.NonPublic/\1BindingFlags.NonPublic | \1BindingFlags.Public/g'
done

sed -i '' -E 's|([[:space:]]+)(</PropertyGroup)|\1\1<PackageId>SamHowes.Microsoft.Build</PackageId>\n\1\2|' "$proj"


if [[ "$1" == "test" ]]; then 
    cat test.cs
    cat "$proj"

    echo "$original_proj" > "$proj"
    echo "$original_cs" > test.cs
fi
