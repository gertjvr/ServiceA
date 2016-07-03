@ECHO OFF
PUSHD %~dp0

IF %1.==. GOTO WithoutBuildNumber

PowerShell.exe -NoProfile -ExecutionPolicy Bypass -Command "& {./build.ps1 -BuildNumber %1}"
GOTO End

:WithoutBuildNumber
	PowerShell.exe -NoProfile -ExecutionPolicy Bypass -Command "& {./build.ps1}"	
	GOTO End

:End