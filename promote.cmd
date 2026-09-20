@echo off
rem Runs promote.ps1 without needing to change PowerShell's script policy:   promote   /   promote -Apply
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0promote.ps1" %*
