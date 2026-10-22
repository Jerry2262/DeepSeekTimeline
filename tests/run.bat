@echo off
setlocal
rem Run on Windows, or invoke this script through cmd.exe from WSL.
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
"%CSC%" /nologo /codepage:65001 /target:exe /main:TopMostRegression /r:System.Windows.Forms.dll /r:System.Drawing.dll /out:"%~dp0TopMostRegression.exe" "%~dp0..\Main.cs" "%~dp0TopMostRegression.cs"
if errorlevel 1 exit /b 1
"%~dp0TopMostRegression.exe"
exit /b %errorlevel%
