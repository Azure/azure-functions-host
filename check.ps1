# Check prereqs for Script Runtime 
# prints diagnostic messages. 

function Check([string] $componentName, [string] $verstr, [Version] $requiredVersion)
{
    if ($verStr[0] -eq "v")  # trim leading 'v'
    { 
        $verStr = $verStr.Substring(1)
    }

    $actualVersion = [Version]::Parse($verstr)

    $msg = $componentName + " " + $verstr;

    if ($actualVersion -lt $requiredVersion)
    {
        Write-Host ("[X] " + $msg +". Error. Must be at least major version " + $requiredVersion) -ForegroundColor Red
        return $false
    } else {
        Write-Host ("[*] " + $msg) -foreground "green"
        return $true
    }
}


Write-Host "Checking dependencies"

# Check VS 
# Use vswhere, which is installed with VS 15.2 and later.  https://github.com/Microsoft/vswhere 
$x= [Environment]::ExpandEnvironmentVariables("%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe")
if(![System.IO.File]::Exists($x)){
    Write-Host "    VSWhere is missing. Is VS 2017 installed? See https://www.visualstudio.com/downloads/ " -ForegroundColor Red
} else {
    $actualVersion = & $x -property catalog_buildVersion   
    $ok = Check "VS 2017" $actualVersion ([Version]::Parse("15.5.0"))
    if (-Not $ok) {
        Write-Host "    You can update VS from the Tools | Extensions and Updates menu."
    }
}

#  Check dotnet
# C:\dev\AFunc\script-core3>dotnet --version
# 2.0.0
$actualVersion = & "dotnet" --version
$ok = Check "dotnet" $actualVersion ([Version]::Parse("2.0"))

# Check Node
$actualVersion = & "node" -v
$ok = Check "node" $actualVersion ([Version]::Parse("8.4.0"))
if (-Not $ok) {
    Write-Host "    You can update node by downloading the latest from https://nodejs.org"
}

# Check NPM 
$actualVersion = & "npm" -v
$ok = Check "npm" $actualVersion ([Version]::Parse("5.0"))

if (-Not $ok) {
    Write-Host "You can upgrade npm by running: "
    Write-Host "    npm install -g npm@latest" 
}

# See here for hints on doing Devenv detection:
# https://stackoverflow.com/questions/42435592/determining-installed-visual-studio-path-for-2017 
# Or here: https://stackoverflow.com/questions/40694598/how-do-i-call-visual-studio-2017-rcs-version-of-msbuild-from-a-bat-file/40702534#40702534