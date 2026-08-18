@echo off
rem DeepSeekTimeline 编译脚本(Windows 自带 csc.exe, 无需安装 SDK)
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /r:System.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /out:"%~dp0DeepSeekTimeline.exe" "%~dp0Main.cs"
if errorlevel 1 (
  echo.
  echo 编译失败
) else (
  echo.
  echo 编译成功: %~dp0DeepSeekTimeline.exe
)
pause
