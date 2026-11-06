@echo off
rem ---------------------------------------------------------------
rem  MyTypingPet build script
rem  Uses the C# compiler that ships with Windows (.NET Framework 4.8).
rem  Every PNG in sprites\<mood>\ is embedded into the exe.
rem ---------------------------------------------------------------
setlocal
cd /d "%~dp0"

set NAME=MyTypingPet

set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo [ERROR] csc.exe not found. .NET Framework 4.x is required.
  pause
  exit /b 1
)

if not exist obj mkdir obj

(for %%s in (idle type fast back enter rest sleep) do (
  if exist "sprites\%%s\" for %%f in ("sprites\%%s\*.png") do echo /resource:"sprites\%%s\%%~nxf",sprites.%%s.%%~nxf
)) > obj\resources.rsp

set ICON=
if exist app.ico set ICON=/win32icon:app.ico

"%CSC%" /nologo /target:winexe /optimize+ /codepage:65001 /out:%NAME%.exe /win32manifest:app.manifest %ICON% /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll @obj\resources.rsp src\*.cs
if errorlevel 1 (
  echo.
  echo [ERROR] Build failed. See the messages above.
  pause
  exit /b 1
)

echo.
echo Build OK: %NAME%.exe
endlocal
