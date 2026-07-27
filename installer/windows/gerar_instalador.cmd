@echo off
setlocal
REM ============================================================
REM  Gera o instalador do Quorum para Windows
REM  Requisitos: .NET 8 SDK + Inno Setup 6 (ISCC no PATH ou no
REM  local padrao de instalacao)
REM ============================================================

cd /d "%~dp0..\.."

echo [1/3] Publicando o aplicativo (self-contained, arquivo unico)...
dotnet publish src\Quorum.App -c Release -r win-x64 --self-contained ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:DebugType=None -p:DebugSymbols=false ^
  -o installer\publish\win-x64
if errorlevel 1 goto :erro

echo [2/3] Localizando o Inno Setup...
set "ISCC=iscc"
where iscc >nul 2>nul
if errorlevel 1 set "ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if not exist "%ISCC%" if not "%ISCC%"=="iscc" (
  echo ERRO: Inno Setup 6 nao encontrado. Instale em https://jrsoftware.org/isdl.php
  goto :erro
)

echo [3/3] Compilando o instalador...
"%ISCC%" installer\windows\instalador_quorum.iss
if errorlevel 1 goto :erro

echo.
echo Pronto! Instalador em installer\windows\saida\
exit /b 0

:erro
echo.
echo A geracao falhou. Veja as mensagens acima.
exit /b 1
