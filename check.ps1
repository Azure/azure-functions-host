# Check prereqs for Script Runtime 
# prints diagnostic messages. 

function Check([string] $componentName, [string] $verstr, $requiredMaxVersion)
{
    if ($verStr[0] -eq "v")  # trim leading 'v'
    { 
        $verStr = $verStr.Substring(1)
    }

    $ver = [Version]::Parse($verstr)

    $msg = $componentName + " " + $verstr;

    if ($ver.Major -lt $requiredMaxVersion)
    {
        Write-Host ("[X] " + $msg +". Error. Must be at least major version " + $requiredMaxVersion) -ForegroundColor Red
        return $false
    } else {
        Write-Host ("[*] " + $msg) -foreground "green"
        return $true
    }
}


Write-Host "Checking dependencies"

#  Check dotnet
# C:\dev\AFunc\script-core3>dotnet --version
# 2.0.0
$out = & "dotnet" --version
$ok = Check "dotnet" $out 2

# Check Node
$out = & "node" -v
$ok = Check "node" $out 6
if (-Not $ok) {
    Write-Host "    You can update node by downloading the latest from https://nodejs.org"
}

# Check NPM 
$out = & "npm" -v
$ok = Check "npm" $out 5

if (-Not $ok) {
    Write-Host "You can upgrade npm by running: "
    Write-Host "    npm install -g npm@latest" 
}

# See here for hints on doing Devenv detection:
# https://stackoverflow.com/questions/42435592/determining-installed-visual-studio-path-for-2017 
# Or here: https://stackoverflow.com/questions/40694598/how-do-i-call-visual-studio-2017-rcs-version-of-msbuild-from-a-bat-file/40702534#40702534