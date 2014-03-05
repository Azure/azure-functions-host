setlocal

set root=c:\t\one
set startcd=%cd%
pushd
rd /q /s %root%
%md %root%
%cd %root%
%md pack
%cd pack
%md SiteExtensions
cd %startcd%
xcopy *.cshtml "%root%\pack\SiteExtensions\One" /e /i
xcopy *.html "%root%\pack\SiteExtensions\One" /e /i
xcopy *.config "%root%\pack\SiteExtensions\One" /e /i
xcopy *.asax "%root%\pack\SiteExtensions\One" /e /i
xcopy *.js "%root%\pack\SiteExtensions\One" /e /i
xcopy *.css "%root%\pack\SiteExtensions\One" /e /i
xcopy *.dll "%root%\pack\SiteExtensions\One" /e /i
xcopy *.pdb "%root%\pack\SiteExtensions\One" /e /i
xcopy *.eot "%root%\pack\SiteExtensions\One" /e /i
xcopy *.svg "%root%\pack\SiteExtensions\One" /e /i
xcopy *.ttf "%root%\pack\SiteExtensions\One" /e /i
xcopy *.woff "%root%\pack\SiteExtensions\One" /e /i
xcopy *.xdt "%root%\pack\SiteExtensions\One" /e /i
rd /q /s %root%\pack\SiteExtensions\One\obj

del one.zip

7za a -r -tzip one.zip %root%\pack\*.*

endlocal