$ErrorActionPreference = 'Stop'
$toolDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
Push-Location $toolDirectory
try {
    npm install
    npx playwright install chromium
    npm run capture
}
finally {
    Pop-Location
}
