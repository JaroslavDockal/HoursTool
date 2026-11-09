@echo off
rem Builds a self-contained single-file HoursTool.exe into the publish folder.
dotnet publish HoursTool.csproj -c Release -r win-x64 -o publish
echo.
echo Done: publish\HoursTool.exe
pause
