@echo off
setlocal enableextensions

title Ingest - try it locally

echo.
echo   Ingest local trial
echo   ------------------
echo   This starts Ingest on your machine using Docker. Nothing else to install by hand.
echo.

REM This script lives in \scripts; the docker-compose.yml is in the project root one level up.
pushd "%~dp0.."

set "DOCKER_DESKTOP=%ProgramFiles%\Docker\Docker\Docker Desktop.exe"

REM ---- 1. Is the docker command available? ----
where docker >nul 2>&1
if errorlevel 1 goto :install_docker

REM ---- 2. Is the Docker engine actually running? ----
docker info >nul 2>&1
if not errorlevel 1 goto :start_ingest

echo Docker is installed, but its engine isn't running yet.
if not exist "%DOCKER_DESKTOP%" (
    echo Please start Docker Desktop manually, wait until it says "Engine running", then run this script again.
    goto :end
)

echo Starting Docker Desktop for you...
start "" "%DOCKER_DESKTOP%"

set /a tries=0
echo Waiting for Docker to be ready ^(the first launch can take a minute^)...
:wait_loop
set /a tries+=1
docker info >nul 2>&1
if not errorlevel 1 goto :start_ingest
if %tries% geq 40 goto :engine_timeout
timeout /t 3 /nobreak >nul 2>&1
goto :wait_loop

:engine_timeout
echo.
echo Docker still isn't ready. Once Docker Desktop shows "Engine running",
echo run this script again.
goto :end

REM ---- 3. Docker isn't installed: offer to install it with winget ----
:install_docker
echo Docker doesn't seem to be installed on this machine.
echo.
where winget >nul 2>&1
if errorlevel 1 goto :no_winget

set "answer=N"
set /p "answer=Install Docker Desktop now using winget? [y/N] "
if /i not "%answer%"=="y" goto :install_declined

echo.
echo Installing Docker Desktop ^(this can take a few minutes^)...
winget install -e --id Docker.DockerDesktop --accept-package-agreements --accept-source-agreements
if errorlevel 1 (
    echo.
    echo The installation didn't finish. You can install Docker Desktop by hand from:
    echo     https://www.docker.com/products/docker-desktop/
    goto :end
)

echo.
echo Docker Desktop is installed. A few manual steps remain ^(Windows requires them^):
echo     1. Open "Docker Desktop" from the Start menu.
echo     2. The first launch may ask you to accept terms, and sometimes to sign out / restart.
echo     3. Wait until it shows "Engine running".
echo     4. Run this script again to start Ingest.
goto :end

:no_winget
echo winget isn't available here, so Docker can't be installed automatically.
echo Please install Docker Desktop by hand from:
echo     https://www.docker.com/products/docker-desktop/
echo Then run this script again.
goto :end

:install_declined
echo.
echo Okay, nothing was installed. Install Docker Desktop when you're ready, then run this script again.
goto :end

REM ---- 4. Everything's ready: build and start Ingest ----
:start_ingest
echo.
echo Building and starting Ingest. The first run downloads and builds images, so please be patient...
echo.
docker compose up -d --build
if errorlevel 1 (
    echo.
    echo Something went wrong while starting the containers. Scroll up to see the error.
    goto :end
)

echo.
echo   Ingest is starting up!
echo   ----------------------
echo   Open in your browser:  http://localhost:8080
echo   API explorer ^(Swagger^): http://localhost:8080/swagger
echo.
echo   Sign in with this API key:
echo       localdev.local-dev-admin-key-change-me
echo.
echo   It can take a few seconds to come up. If the page doesn't load, wait a moment and refresh.
echo.
echo   To stop Ingest later, run this from the project folder:
echo       docker compose down

:end
popd >nul 2>&1
echo.
pause
endlocal
