@echo off
title Carton Label - Settings
set EXE=%~dp0StandAlone.CartonUi\bin\Debug\net11.0-windows\StandAlone.CartonUi.exe
if not exist "%EXE%" (
    echo ERROR: Could not find StandAlone.CartonUi.exe
    echo Expected at: %EXE%
    pause
    exit /b 1
)
start "" "%EXE%" --settings
