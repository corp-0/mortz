@echo off
rem Thin wrapper; tools must run from the repo root.
pushd "%~dp0.."
dotnet run --project tools -- convert-lxl %*
set EXITCODE=%ERRORLEVEL%
popd
exit /b %EXITCODE%
