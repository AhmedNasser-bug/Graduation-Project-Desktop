#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")"

MODE="${1:-run}"
REPO_PARENT="$(cd .. && pwd)"
BACKEND_DIR="${NEPHROEDGE_BACKEND_DIR:-$REPO_PARENT/backend}"
BACKEND_BRANCH="${NEPHROEDGE_BACKEND_BRANCH:-feat/analytics-sync-refactor}"

if [ ! -f "$BACKEND_DIR/Services_Unit/Services_Unit.csproj" ]; then
  echo "[Desktop Setup] Cloning xByteA/Graduation-Project-Backend into $BACKEND_DIR ..."
  git clone https://github.com/xByteA/Graduation-Project-Backend.git "$BACKEND_DIR"
fi

if [ ! -f "$BACKEND_DIR/Persistence_Unit/Interfaces/IOrderHandshakeRepository.cs" ]; then
  echo "[Desktop Setup] Switching backend to compatible branch '$BACKEND_BRANCH' ..."
  git -C "$BACKEND_DIR" fetch origin "$BACKEND_BRANCH"
  git -C "$BACKEND_DIR" checkout "$BACKEND_BRANCH"
fi

if [ "$MODE" = "--docker" ]; then
  STAGE_DIR="$(mktemp -d)"
  trap 'rm -rf "$STAGE_DIR"' EXIT
  mkdir -p "$STAGE_DIR/backend" "$STAGE_DIR/desktop"

  for sub in Persistence_Unit Middleware_Unit Services_Unit; do
    cp -a "$BACKEND_DIR/$sub" "$STAGE_DIR/backend/$sub"
  done
  if [ -d "$BACKEND_DIR/Tests_Unit/Test_Data" ]; then
    mkdir -p "$STAGE_DIR/backend/Tests_Unit"
    cp -a "$BACKEND_DIR/Tests_Unit/Test_Data" "$STAGE_DIR/backend/Tests_Unit/Test_Data"
  fi
  cp -a . "$STAGE_DIR/desktop"
  find "$STAGE_DIR" -type d \( -name bin -o -name obj -o -name .git -o -name TestArtifacts -o -name RuntimeStores \) -prune -exec rm -rf {} +

  echo "[Desktop Setup] Building Docker test image 'nephroguard-desktop-tests' ..."
  docker build -f "$STAGE_DIR/desktop/Dockerfile" -t nephroguard-desktop-tests "$STAGE_DIR"
  echo "[Desktop Setup] Running Avalonia Headless UI + Clinical Safety Suite inside container ..."
  exec docker run --rm nephroguard-desktop-tests
fi

echo "[Desktop Setup] Building NephroEdge.Desktop.slnx ..."
dotnet build NephroEdge.Desktop.slnx /p:UseSharedCompilation=false -nodeReuse:false -maxCpuCount:1

if [ "$MODE" != "--skip-tests" ]; then
  echo "[Desktop Setup] Running Avalonia Headless UI + Clinical Safety Suite ..."
  dotnet run --project NephroEdge.Desktop.HeadlessTests --no-build
fi

if [ "$MODE" = "--verify-only" ]; then
  echo "[Desktop Setup] Verification completed cleanly."
  exit 0
fi

echo "[Desktop Setup] Launching NephroEdge Clinic Workstation ..."
exec dotnet run --project NephroEdge.Desktop --no-build
