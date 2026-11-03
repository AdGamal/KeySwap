@echo off
rem Builds KeySwap.exe with the C# compiler that comes with Windows (.NET Framework 4). Nothing to install.
setlocal
cd /d "%~dp0"

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo Could not find the C# compiler that comes with Windows ^(.NET Framework 4^).
  exit /b 1
)
rem UI Automation (used to skip password fields) lives in the WPF folder next to the compiler.
for %%I in ("%CSC%") do set "FWDIR=%%~dpI"
if not exist build mkdir build

"%CSC%" /nologo /codepage:65001 /target:exe /optimize+ /out:build\Tests.exe Converter.cs AutoFix.cs CommonWords.cs Spelling.cs Layouts.cs Tests.cs || exit /b 1
build\Tests.exe %* || exit /b 1

"%CSC%" /nologo /codepage:65001 /target:winexe /optimize+ /platform:anycpu /win32icon:KeySwap.ico ^
  /lib:"%FWDIR%WPF" /r:System.Windows.Forms.dll /r:System.Drawing.dll ^
  /r:UIAutomationClient.dll /r:UIAutomationTypes.dll /r:WindowsBase.dll ^
  /out:build\KeySwap.exe Converter.cs AutoFix.cs CommonWords.cs Spelling.cs Layouts.cs App.cs || exit /b 1

echo Built %~dp0build\KeySwap.exe
