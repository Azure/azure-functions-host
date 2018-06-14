# Check prereqs for Script Runtime 
# prints diagnostic messages. 

function Check([string] $componentName, [string] $verstr, [Version] $requiredVersion)
{
    if ($verStr[0] -eq "v")  # trim leading 'v'
    { 
        $verStr = $verStr.Substring(1)
    }
	
    # Version could be a beta, like 2.1.200-preview-007576
    $x = $verstr;
    $idxDash = $x.IndexOf('-')
    if ($idxDash -gt 0) {
        $x = $x.Substring(0, $idxDash)
    }

    $actualVersion = [Version]::Parse($x)

    $msg = $componentName + " " + $verstr;

    if ($actualVersion -lt $requiredVersion)
    {
        Write-Host ("[X] " + $msg +". Error. Must be at least major version " + $requiredVersion) -ForegroundColor Red
        return $false
    } else {
        Write-Host -NoNewline ("[*] " + $msg) -foreground "green"
        Write-Host "    (must be at least" $requiredVersion ")"
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
    $installedVersions = & $x -property catalog_buildVersion   
    
    # could be either a string or an array of strings. 
    if ($installedVersions  -is [array]) {
        # as an array, take the latest version on the machine
        $latest =  [Version]::Parse($installedVersions[0])
        Write-Host "    Multiple VS versions installed:"
        foreach($ver in $installedVersions) {
            $verCurrent = [Version]::Parse($ver)
            Write-Host "      " $verCurrent
            if ($verCurrent -gt $latest) {
                $latest = $verCurrent
            }
        }    
    } else {
        $latest = $installedVersions;
    }
    

    $ok = Check "VS 2017" ($latest.ToString()) ([Version]::Parse("15.7.0"))
    if (-Not $ok) {
        Write-Host "    You can update VS from the Tools | Extensions and Updates menu."
    }
}

#  Check dotnet
# C:\dev\AFunc\script-core3>dotnet --version
# 2.0.0
$actualVersion = & "dotnet" --version
$ok = Check "dotnet" $actualVersion ([Version]::Parse("2.1.300"))
if (-Not $ok) {
    Write-Host "You can update by installing '.NET Core 2.1 SDK' from https://www.microsoft.com/net/download/windows"
    Write-Host 
}

# Check Node
$actualVersion = & "node" -v
$ok = Check "node" $actualVersion ([Version]::Parse("8.4.0"))
if (-Not $ok) {
    Write-Host "    You can update node by downloading the latest from https://nodejs.org"
    Write-Host 
}

# Check NPM 
$actualVersion = & "npm" -v
$ok = Check "npm" $actualVersion ([Version]::Parse("5.0"))

if (-Not $ok) {
    Write-Host "You can upgrade npm by running: "
    Write-Host "    npm install -g npm@latest" 
    Write-Host 
}

# Check Java
$javaFullVersion = & "java" -version 2>&1
$javaVersion = $javaFullVersion[0].toString().Split(' ')
$javaVersion = $javaVersion[2].Trim('"')
$underscoreIndex = $javaVersion.IndexOf('_')
$actualVersion = $javaVersion.Substring(0, $underscoreIndex)
$ok = Check "java" $actualVersion ([Version]::Parse("1.8.0"))

if (-Not $ok) {
    Write-Host "    You can udpate java by downloading from http://www.oracle.com/technetwork/java/javase/downloads/jdk8-downloads-2133151.html" 
    Write-Host 
}
$ok = Test-Path 'env:JAVA_HOME'
if (-Not $ok) {
	Write-Host "JAVA_HOME Environment variable not set. Set it to JDK folder. Example c:\Program Files\Java\jdk1.8.0_171" -ForegroundColor Red
	Write-Host 
}


# See here for hints on doing Devenv detection:
# https://stackoverflow.com/questions/42435592/determining-installed-visual-studio-path-for-2017 
# Or here: https://stackoverflow.com/questions/40694598/how-do-i-call-visual-studio-2017-rcs-version-of-msbuild-from-a-bat-file/40702534#40702534