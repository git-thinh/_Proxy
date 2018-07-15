@rem Installs TcpGatewayService.exe as a Windows service.

@echo off & setlocal
set iu=InstallUtil.exe
if not exist "%iu%" set iu=%SystemRoot%\Microsoft.NET\Framework\v2.0.50727\InstallUtil.exe
if not exist "%iu%" (echo InstallUtil.exe not found & exit /b 9 )

"%iu%" %* TcpGatewayService.exe
if errorlevel 1 exit /b 9

if not "%1"=="/uninstall" net start TcpGatewayService
