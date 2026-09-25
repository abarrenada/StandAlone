@echo off
rem Starts the StandAlone Data Lake web UI on this laptop and opens it in the browser.
rem Other PCs on the plant network: http://DALLAP7YR37G4.na.int.grp:5080
rem Close this window to stop it.
cd /d "%~dp0..\StandAlone.DataLakeUi"
start "" http://localhost:5080
dotnet run -c Release
