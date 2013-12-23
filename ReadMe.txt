*** Steps for installing Simple Batch ***

Install Visual Studio 2013

From Web Platform Installer install Windows Azure SDK for .NET (VS2013) 2.2
This is needed for the storage emulator.

Install Git
http://code.google.com/p/msysgit/downloads/list?can=3&q=official+Git

Recommended UI tool: Install TortoiseGit
http://code.google.com/p/tortoisegit/wiki/Download


*** Build notes ***

From the Start Menu run the Windows Azure Storage Emulator
- A console window will show up, then disappear, and a Tray Icon will show up (a blue Windows logo)
- Right click on the Tray Icon and select Start Storage Emulator

The project site is at codeplex: https://azuresimplebatch.codeplex.com

Download sources from Codeplex
    git clone https://git01.codeplex.com/azuresimplebatch 
This will create a directory "azuresimplebatch" in your current directory and download there.

You can't check in passwords. But passwords must be specified in *.cscfg files.
GIT will ignore those files, but you'll see files like this:
    CopyMeAndRenameTo_ServiceConfiguration.Local.cscfg

Copy and rename it to:
    ServiceConfiguration.Local.cscfg

This will make you local build pass, but won't get checked in by Git.

NuGet will pull down depedencies. 

