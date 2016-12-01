#!/bin/bash

CONFIG="${1:-Release}"
SLNDIR="$( dirname ${0} )"

msbuild WebJobs.Script.proj /t:RestorePackages
msbuild WebJobs.Script.sln "/p:Configuration=${CONFIG};SolutionDir=${SLNDIR}"
