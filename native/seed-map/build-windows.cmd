@echo off
setlocal
if not exist "%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe" goto missing
for /f "usebackq delims=" %%V in (`"%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "VS=%%V"
if not defined VS goto missing
call "%VS%\VC\Auxiliary\Build\vcvars64.bat" >nul
if errorlevel 1 exit /b 1
cl /nologo /LD /O2 /MT /std:c11 /D_CRT_SECURE_NO_WARNINGS "%~dp0seed_map.c" "%~dp0cubiomes\*.c" /link /OUT:cmc_seed_map.dll
exit /b %errorlevel%
:missing
echo Install the Visual Studio C++ x64 build tools to build the seed map.
exit /b 1
