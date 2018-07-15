@rem Installs TcpGatewayService.exe as a Windows service.

@echo off

net stop TcpGatewayService

call InstallService.bat /uninstall
