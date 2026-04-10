#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
VERSION="${SESSIONGUARD_VERSION:-0.1.0}"
ARCH="${SESSIONGUARD_DEB_ARCH:-amd64}"
CONFIGURATION="${CONFIGURATION:-Release}"
ARTIFACT_DIR="${SESSIONGUARD_ARTIFACT_DIR:-$ROOT_DIR/artifacts}"
STAGING_ROOT="$ROOT_DIR/tmp/deb/sessionguard-agent"
PACKAGE_NAME="sessionguard-agent_${VERSION}_${ARCH}.deb"

rm -rf "$STAGING_ROOT"
mkdir -p "$STAGING_ROOT/DEBIAN"
mkdir -p "$STAGING_ROOT/opt/sessionguard/agent"
mkdir -p "$STAGING_ROOT/opt/sessionguard/tray"
mkdir -p "$STAGING_ROOT/lib/systemd/system"
mkdir -p "$STAGING_ROOT/etc/sessionguard"
mkdir -p "$STAGING_ROOT/etc/xdg/autostart"
mkdir -p "$STAGING_ROOT/usr/share/icons/hicolor/scalable/apps"
mkdir -p "$ARTIFACT_DIR"

dotnet publish "$ROOT_DIR/src/Agent.Linux/Agent.Linux.csproj" \
  -c "$CONFIGURATION" \
  -o "$STAGING_ROOT/opt/sessionguard/agent" \
  --no-self-contained \
  /p:UseAppHost=false \
  /p:Version="$VERSION" \
  /p:InformationalVersion="$VERSION"

dotnet publish "$ROOT_DIR/src/Agent.Tray/Agent.Tray.csproj" \
  -c "$CONFIGURATION" \
  -o "$STAGING_ROOT/opt/sessionguard/tray" \
  --no-self-contained \
  /p:UseAppHost=false \
  /p:Version="$VERSION" \
  /p:InformationalVersion="$VERSION"

sed "s/@VERSION@/$VERSION/g; s/@ARCH@/$ARCH/g" "$ROOT_DIR/packaging/deb/control.template" > "$STAGING_ROOT/DEBIAN/control"
cp "$ROOT_DIR/packaging/deb/postinst" "$STAGING_ROOT/DEBIAN/postinst"
cp "$ROOT_DIR/packaging/deb/prerm" "$STAGING_ROOT/DEBIAN/prerm"
cp "$ROOT_DIR/packaging/deb/conffiles" "$STAGING_ROOT/DEBIAN/conffiles"
cp "$ROOT_DIR/packaging/deb/sessionguard-agent.service" "$STAGING_ROOT/lib/systemd/system/sessionguard-agent.service"
cp "$ROOT_DIR/packaging/deb/sessionguard-agent.env" "$STAGING_ROOT/etc/sessionguard/agent.env"
cp "$ROOT_DIR/packaging/deb/sessionguard-tray.desktop" "$STAGING_ROOT/etc/xdg/autostart/sessionguard-tray.desktop"
cp "$ROOT_DIR/packaging/deb/sessionguard.svg" "$STAGING_ROOT/usr/share/icons/hicolor/scalable/apps/sessionguard.svg"

chmod 0755 "$STAGING_ROOT/DEBIAN/postinst" "$STAGING_ROOT/DEBIAN/prerm"
chmod 0644 "$STAGING_ROOT/DEBIAN/control"
chmod 0644 "$STAGING_ROOT/DEBIAN/conffiles"
chmod 0644 "$STAGING_ROOT/lib/systemd/system/sessionguard-agent.service"
chmod 0644 "$STAGING_ROOT/etc/sessionguard/agent.env"
chmod 0644 "$STAGING_ROOT/etc/xdg/autostart/sessionguard-tray.desktop"
chmod 0644 "$STAGING_ROOT/usr/share/icons/hicolor/scalable/apps/sessionguard.svg"

dpkg-deb --build --root-owner-group "$STAGING_ROOT" "$ARTIFACT_DIR/$PACKAGE_NAME"
dpkg-deb --info "$ARTIFACT_DIR/$PACKAGE_NAME"
dpkg-deb --contents "$ARTIFACT_DIR/$PACKAGE_NAME" | grep -E "Agent\\.Linux\\.dll|Agent\\.Tray\\.dll|sessionguard-agent\\.service|sessionguard-tray\\.desktop"

echo "$ARTIFACT_DIR/$PACKAGE_NAME"
