@echo off
cd "%~dp0.."
%dotnet_framework_dir%\csc.exe /nologo src\TcpStressTest.cs
