*** Steps for installing Simple Batch ***


Install VS 2012 with SP1


Install Oct 2012 Azure SDK (Not 1.8, not 2.0. 2.0 has breaking changes)
This is needed to load the azure projects in VS, as well as for the compute emulator. 
Via Web Platform Installer: http://go.microsoft.com/fwlink/?LinkID=254364


Install GIT 
http://code.google.com/p/msysgit/downloads/list?can=3&q=official+Git

Recommended UI tool: Install TortoiseGIT
http://code.google.com/p/tortoisegit/wiki/Download




*** Build notes ***
The project site is at codeplex: https://azuresimplebatch.codeplex.com 

Download sources from Codeplex
    git clone https://git01.codeplex.com/azuresimplebatch 
This will create a directory "azuresimplebatch" in your current directory and download there. 

You can't check in passwords. But passwords must be specified in *.cscfg files. 
GIT will ignore those files, but you'll see files like this:
    CopyMeAndRenameTo_ServiceConfiguration.Local.cscfg

Copy and rename it to:
    ServiceConfiguration.Local.cscfg

This will make you local build pass, but won't get checked in by GIT. 

Nuget will pull down depedencies. 

