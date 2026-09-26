@echo off
rem Baut dist\ysfUtil.exe (framework-abhaengig, braucht die .NET 8 Desktop Runtime).
cd /d "%~dp0"
taskkill /im ysfUtil.exe /f >nul 2>&1
dotnet publish ysfUtil.csproj -c Release -o dist -nologo -v q
