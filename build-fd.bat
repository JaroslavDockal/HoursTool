@echo off
rem Builds a framework-dependent single-file HoursTool.exe (needs .NET 8 Desktop Runtime installed).
rem Smaller than build.bat, but the target PC must have the runtime:
rem https://dotnet.microsoft.com/download/dotnet/8.0  (.NET Desktop Runtime 8.x, x64)
dotnet publish HoursTool.csproj -c Release -r win-x64 -p:SelfContained=false -o publish-fd
echo.
echo Done: publish-fd\HoursTool.exe
pause
