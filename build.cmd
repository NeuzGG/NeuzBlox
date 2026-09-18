@echo off
setlocal
rem ---------------------------------------------------------------
rem  NeuzBlox build script
rem  Uses the C# compiler that ships with Windows - no SDK needed.
rem ---------------------------------------------------------------

set ROOT=%~dp0
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo Could not find the .NET Framework C# compiler.
  exit /b 1
)

if not exist "%ROOT%build" mkdir "%ROOT%build"

echo [1/2] icon
"%CSC%" /nologo /target:exe /out:"%ROOT%build\MakeIcon.exe" /r:System.dll /r:System.Drawing.dll "%ROOT%tools\MakeIcon.cs" || exit /b 1
"%ROOT%build\MakeIcon.exe" "%ROOT%build\NeuzBlox.ico" || exit /b 1

echo [2/2] NeuzBlox.exe
"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 ^
  /out:"%ROOT%NeuzBlox.exe" ^
  /win32icon:"%ROOT%build\NeuzBlox.ico" ^
  /win32manifest:"%ROOT%src\app.manifest" ^
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Security.dll ^
  "%ROOT%src\*.cs" || exit /b 1

echo.
echo Done -^> %ROOT%NeuzBlox.exe
endlocal
