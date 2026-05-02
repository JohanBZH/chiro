#!/bin/bash
# Script to build and publish the standalone executable for Windows

cd "$(dirname "$0")"

export DOTNET_CLI_HOME=/tmp
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1

echo "Building and publishing Chiro.App for win-x64..."

dotnet publish ./Chiro.App/Chiro.App.csproj \
    -c Release \
    -r win-x64 \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -o ./publish_output

echo "Publish complete. Output is in the ./publish_output directory."
echo "Make sure to provide an appsettings.json file in the same directory as the executable with the correct PostgreSQL connection string."
