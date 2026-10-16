@echo off
rem ipinfo.exe 단일 실행파일 만들기 (결과: dist 폴더)
chcp 65001 >nul
cd /d "%~dp0"
echo 빌드 중입니다. 1~2분 걸립니다...
dotnet publish IpInfo.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o dist > build.log 2>&1
set ERR=%ERRORLEVEL%
echo EXIT=%ERR%>> build.log
if not "%ERR%"=="0" (
  echo 빌드 실패. build.log 를 확인하세요.
  pause
  exit /b %ERR%
)
if exist data xcopy /e /i /y /q data dist\data >nul
if exist dist\ipinfo.pdb del /q dist\ipinfo.pdb
echo 완료: dist\ipinfo.exe
echo 완료>> build.log
