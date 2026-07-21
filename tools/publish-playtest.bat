@echo off
pushd "%~dp0.."
dotnet run --project tools -- publish-playtest %*
set EXITCODE=%ERRORLEVEL%
popd
exit /b %EXITCODE%
