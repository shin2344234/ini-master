@echo off
rem Builds ExampleMod.asi with MSVC Build Tools 2022. Run from any shell.
setlocal
set "VCVARS=C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvars64.bat"
call "%VCVARS%" >nul 2>&1 || (echo vcvars64.bat not found & exit /b 1)
cd /d "%~dp0"
if not exist build mkdir build
rc /nologo /fo build\ExampleMod.res ExampleMod.rc || exit /b 1
cl /nologo /std:c++20 /O2 /EHsc /LD /Fobuild\ /Febuild\ExampleMod.asi ExampleMod.cpp build\ExampleMod.res /link /OPT:REF || exit /b 1
echo built build\ExampleMod.asi
