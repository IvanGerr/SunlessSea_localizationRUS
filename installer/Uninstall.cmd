@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "Start-Process powershell.exe -Verb RunAs -Wait -ArgumentList '-NoProfile -NoExit -ExecutionPolicy Bypass -File ""%~dp0Uninstall-Russian.ps1""'"
