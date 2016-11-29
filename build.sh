#!/bin/bash

CONFIG="${1:-Release}"
SLNDIR="$( dirname ${0} )"

msbuild WebJobs.Script.proj "/p:Configuration=${CONFIG};SolutionDir=${SLNDIR}"
