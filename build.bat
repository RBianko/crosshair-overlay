@echo off
cd /d "%~dp0"
if not exist crosshair.ico powershell -NoProfile -ExecutionPolicy Bypass -File make_icon.ps1
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /codepage:65001 /target:winexe /optimize+ /out:Crosshair.exe /win32icon:crosshair.ico /resource:crosshair.ico,crosshair.ico /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll Crosshair.cs Panel.cs AssemblyInfo.cs
