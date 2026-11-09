#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT_DIR="$ROOT_DIR/EveOPreview.Mac"
APP_NAME="EVE-O Preview"
BUNDLE_NAME="${APP_NAME}.app"
RUNTIME="${RUNTIME:-osx-arm64}"
CONFIGURATION="${CONFIGURATION:-Release}"
ICON_SOURCE="$PROJECT_DIR/icon.png"
PUBLISH_DIR="$PROJECT_DIR/bin/$CONFIGURATION/net8.0/$RUNTIME/publish"
DIST_DIR="${DIST_DIR:-$ROOT_DIR/dist}"
APP_BUNDLE_DIR="$DIST_DIR/$BUNDLE_NAME"
DMG_PATH="$DIST_DIR/eve-o-preview-mac-${RUNTIME}.dmg"

echo "Building macOS app ($RUNTIME, $CONFIGURATION)..."
dotnet publish "$PROJECT_DIR/EveOPreview.Mac.csproj" \
  -c "$CONFIGURATION" \
  -r "$RUNTIME" \
  --self-contained true \
  /p:PublishSingleFile=false

echo "Creating .app bundle..."
rm -rf "$APP_BUNDLE_DIR"
mkdir -p "$APP_BUNDLE_DIR/Contents/MacOS" "$APP_BUNDLE_DIR/Contents/Resources"

# Copy the full publish folder so native/runtime dependencies are included.
cp -R "$PUBLISH_DIR"/. "$APP_BUNDLE_DIR/Contents/MacOS/"

ICONSET_DIR="$(mktemp -d)"
mkdir -p "$ICONSET_DIR/AppIcon.iconset"
sips -z 16 16 "$ICON_SOURCE" --out "$ICONSET_DIR/AppIcon.iconset/icon_16x16.png" >/dev/null
sips -z 32 32 "$ICON_SOURCE" --out "$ICONSET_DIR/AppIcon.iconset/icon_16x16@2x.png" >/dev/null
sips -z 32 32 "$ICON_SOURCE" --out "$ICONSET_DIR/AppIcon.iconset/icon_32x32.png" >/dev/null
sips -z 64 64 "$ICON_SOURCE" --out "$ICONSET_DIR/AppIcon.iconset/icon_32x32@2x.png" >/dev/null
sips -z 128 128 "$ICON_SOURCE" --out "$ICONSET_DIR/AppIcon.iconset/icon_128x128.png" >/dev/null
sips -z 256 256 "$ICON_SOURCE" --out "$ICONSET_DIR/AppIcon.iconset/icon_128x128@2x.png" >/dev/null
sips -z 256 256 "$ICON_SOURCE" --out "$ICONSET_DIR/AppIcon.iconset/icon_256x256.png" >/dev/null
sips -z 512 512 "$ICON_SOURCE" --out "$ICONSET_DIR/AppIcon.iconset/icon_256x256@2x.png" >/dev/null
sips -z 512 512 "$ICON_SOURCE" --out "$ICONSET_DIR/AppIcon.iconset/icon_512x512.png" >/dev/null
cp "$ICON_SOURCE" "$ICONSET_DIR/AppIcon.iconset/icon_512x512@2x.png"
iconutil -c icns "$ICONSET_DIR/AppIcon.iconset" -o "$APP_BUNDLE_DIR/Contents/Resources/AppIcon.icns"
rm -rf "$ICONSET_DIR"

cat > "$APP_BUNDLE_DIR/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleDisplayName</key>
  <string>EVE-O Preview</string>
  <key>CFBundleExecutable</key>
  <string>EveOPreview.Mac</string>
  <key>CFBundleIdentifier</key>
  <string>com.eveoplus.eveopreview.mac</string>
  <key>CFBundleIconFile</key>
  <string>AppIcon</string>
  <key>CFBundleName</key>
  <string>EVE-O Preview</string>
  <key>CFBundlePackageType</key>
  <string>APPL</string>
  <key>CFBundleShortVersionString</key>
  <string>0.2.0</string>
  <key>CFBundleVersion</key>
  <string>0.2.0</string>
  <key>LSMinimumSystemVersion</key>
  <string>12.0</string>
  <key>NSHighResolutionCapable</key>
  <true/>
</dict>
</plist>
PLIST

chmod +x "$APP_BUNDLE_DIR/Contents/MacOS/EveOPreview.Mac"

echo "Signing and verifying .app bundle..."
codesign --force --deep --sign - "$APP_BUNDLE_DIR"
codesign --verify --deep --strict "$APP_BUNDLE_DIR"

echo "Creating DMG at $DMG_PATH..."
mkdir -p "$DIST_DIR"
rm -f "$DMG_PATH"
hdiutil create -volname "EVE-O Preview" -srcfolder "$APP_BUNDLE_DIR" -ov -format UDZO "$DMG_PATH"

echo "Done:"
echo "  App bundle: $APP_BUNDLE_DIR"
echo "  DMG:        $DMG_PATH"
