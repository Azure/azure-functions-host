#!/bin/bash

mkdir ~/github
cd ~/github
git clone https://github.com/Azure/azure-functions-host.git
cd azure-functions-host
git checkout dev

cd tools/Crank/Agent
sudo find . -name "*.sh" -exec sudo chmod +xr {} \;
sudo find . -name "*.ps1" -exec sudo chmod +xr {} \;

Linux/install-powershell.sh

./setup-crank-agent-raw.ps1 $1 -Verbose
