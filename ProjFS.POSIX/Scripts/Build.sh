#!/bin/bash

CONFIGURATION=$1
if [ -z $CONFIGURATION ]; then
  CONFIGURATION=Debug
fi

SCRIPTDIR=$(dirname ${BASH_SOURCE[0]})
SRCDIR=$SCRIPTDIR/../..
ROOTDIR=$SRCDIR/..
PACKAGES=$ROOTDIR/packages

PROJFS=$SRCDIR/ProjFS.POSIX

# If we're building the Profiling(Release) configuration, remove Profiling() for building .NET code
if [ "$CONFIGURATION" == "Profiling(Release)" ]; then
  CONFIGURATION=Release
fi

echo "Restoring and building ProjFS.POSIX packages..."
dotnet restore $PROJFS/PrjFSLib.POSIX.Managed/PrjFSLib.POSIX.Managed.csproj /p:Configuration=$CONFIGURATION /p:Platform=x64 --packages $PACKAGES || exit 1
dotnet build $PROJFS/PrjFSLib.POSIX.Managed/PrjFSLib.POSIX.Managed.csproj /p:Configuration=$CONFIGURATION /p:Platform=x64 || exit 1
