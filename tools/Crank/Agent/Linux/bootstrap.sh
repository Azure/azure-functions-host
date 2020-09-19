#!/bin/bash

mkdir /home/Functions/github
cd /home/Functions/github
git clone https://github.com/Azure/azure-functions-host.git
cd azure-functions-host
git checkout dev

cd tools/Crank/Agent
chmod -R +x *.sh
chmod -R +x *.ps1
Linux/install-powershell.sh
sudo -H -u Functions ./setup-crank-agent.ps1 -CrankBranch master
