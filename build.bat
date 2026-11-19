@echo off
rem Build KeePass Cerberus (Release) with the .NET SDK - no Visual Studio needed.
setlocal
set ROOT=%~dp0
set DOTNET_CLI_TELEMETRY_OPTOUT=1
"%ROOT%tools\dotnet\dotnet.exe" msbuild "%ROOT%src\KeePass\KeePass.csproj" -nologo -v:m ^
  -p:Configuration=Release -p:TargetFrameworkVersion=v4.8 -p:SignAssembly=false ^
  -p:FrameworkPathOverride="%ROOT%tools\refasm\build\.NETFramework\v4.8" ^
  -p:CustomAfterMicrosoftCommonTargets="%ROOT%tools\build.targets" ^
  -p:OutputPath="%ROOT%bin\\"
endlocal
