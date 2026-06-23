#!/usr/bin/env bash
set -euo pipefail

RID="${1}"
VERSION="${2}"
APP_NAME="KB.AI.Usage"
BUNDLE="${APP_NAME}.app"
PUBLISH_DIR="publish/${RID}"
DIST_DIR="dist"

mkdir -p "${DIST_DIR}"
rm -rf "${BUNDLE}"
mkdir -p "${BUNDLE}/Contents/MacOS"
mkdir -p "${BUNDLE}/Contents/Resources"

cp "${PUBLISH_DIR}/AiUsage.App" "${BUNDLE}/Contents/MacOS/AiUsage.App"
chmod +x "${BUNDLE}/Contents/MacOS/AiUsage.App"

sed "s/{{VERSION}}/${VERSION}/g" packaging/macos/Info.plist > "${BUNDLE}/Contents/Info.plist"

ICNS="packaging/icon/icon.icns"
if [ -f "${ICNS}" ]; then
  cp "${ICNS}" "${BUNDLE}/Contents/Resources/icon.icns"
fi

ditto -c -k --keepParent "${BUNDLE}" "${DIST_DIR}/${APP_NAME}-${RID}.zip"

rm -rf "${BUNDLE}"
