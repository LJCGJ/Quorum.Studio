@echo off
setlocal
REM ============================================================
REM  Publica o Quorum para as seis plataformas (x64 e ARM64 de
REM  Windows, Linux e macOS) e gera um .zip portatil de cada.
REM
REM  Requisito: .NET 8 SDK. Roda inteiro na sua maquina x64 —
REM  o publish self-contained cruza-compila sem hardware ARM.
REM
REM  Manter VERSION igual ao AppVersion de windows\instalador_quorum.iss
REM ============================================================
set VERSION=5.0.1

cd /d "%~dp0.."
if not exist installer\portateis mkdir installer\portateis

for %%R in (win-x64 win-arm64 linux-x64 linux-arm64 osx-x64 osx-arm64) do (
  echo.
  echo ===== %%R =====
  dotnet publish src\Quorum.App -c Release -r %%R --self-contained ^
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true ^
    -p:DebugType=None -p:DebugSymbols=false ^
    -o installer\publish\%%R
  if errorlevel 1 goto :erro

  powershell -NoProfile -Command "Compress-Archive -Path 'installer\publish\%%R\*' -DestinationPath 'installer\portateis\Quorum-%VERSION%-%%R.zip' -Force"
  if errorlevel 1 goto :erro
)

echo.
echo Pronto! Pacotes em installer\portateis\
echo.
echo Observacoes para quem baixar:
echo  - Windows: extrair e executar Quorum.App.exe (o instalador continua
echo    sendo a via recomendada para win-x64)
echo  - Linux/macOS: extrair e dar permissao antes de executar:
echo        chmod +x Quorum.App
echo    (o zip feito no Windows nao preserva a permissao de execucao)
exit /b 0

:erro
echo.
echo A publicacao falhou. Veja as mensagens acima.
exit /b 1
