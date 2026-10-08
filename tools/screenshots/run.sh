#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")"
npm install
npx playwright install chromium
npm run capture
