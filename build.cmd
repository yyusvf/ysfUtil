@echo off
rem Builds dist\ysfUtil.exe (framework-dependent, needs the .NET 8 Desktop Runtime).
cd /d "%~dp0"
taskkill /im ysfUtil.exe /f >nul 2>&1
dotnet publish ysfUtil.csproj -c Release -o dist -nologo -v q
