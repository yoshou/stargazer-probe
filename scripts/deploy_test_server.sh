#!/usr/bin/env bash
set -euo pipefail

: "${REMOTE_HOST:?REMOTE_HOST is required (e.g. export REMOTE_HOST=my-server)}"
REMOTE_DIR="${REMOTE_DIR:-~/stargazer-probe-test-server}"
PORT="${PORT:-50051}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
LOCAL_DIR="${LOCAL_DIR:-${REPO_ROOT}/test-server-cpp}"

if [[ ! -d "${LOCAL_DIR}" ]]; then
  echo "[deploy] ERROR: local dir not found: ${LOCAL_DIR}" >&2
  exit 1
fi

command -v ssh >/dev/null 2>&1 || { echo "[deploy] ERROR: ssh not found" >&2; exit 1; }
command -v rsync >/dev/null 2>&1 || { echo "[deploy] ERROR: rsync not found" >&2; exit 1; }

echo "[deploy] Remote: ${REMOTE_HOST}:${REMOTE_DIR}"
echo "[deploy] Local : ${LOCAL_DIR}"

echo "[deploy] (1/4) Ensure remote dir exists"
ssh "${REMOTE_HOST}" "mkdir -p ${REMOTE_DIR}"

echo "[deploy] (2/4) rsync source -> remote (excluding build/)"
rsync -az \
  --exclude '.git/' \
  --exclude 'build/' \
  --exclude '*.user' \
  --exclude '*.suo' \
  "${LOCAL_DIR}/" "${REMOTE_HOST}:${REMOTE_DIR}/"

echo "[deploy] (3/4) Stop server if running"
ssh "${REMOTE_HOST}" bash -s -- "${REMOTE_DIR}" "${PORT}" <<'REMOTE_STOP' || true
set -u
REMOTE_DIR="$1"
PORT="$2"

# Expand leading ~ safely
if [[ "$REMOTE_DIR" == ~* ]]; then
  REMOTE_DIR="${REMOTE_DIR/#\~/$HOME}"
fi

cd "$REMOTE_DIR" 2>/dev/null || {
  echo "[remote] WARN: cannot cd to $REMOTE_DIR" >&2
  exit 0
}

if [[ -f server.pid ]]; then
  pid="$(cat server.pid 2>/dev/null || true)"
  if [[ -n "$pid" ]] && ps -p "$pid" >/dev/null 2>&1; then
    echo "[remote] killing pid=$pid"
    kill "$pid" >/dev/null 2>&1 || true
    for _ in 1 2 3 4 5 6 7 8 9 10; do
      ps -p "$pid" >/dev/null 2>&1 || break
      sleep 0.2
    done
    if ps -p "$pid" >/dev/null 2>&1; then
      echo "[remote] SIGKILL pid=$pid"
      kill -9 "$pid" >/dev/null 2>&1 || true
    fi
  else
    echo "[remote] server.pid exists but process not running (pid=$pid)"
  fi
else
  echo "[remote] no server.pid"
fi

# Best-effort cleanup regardless of pid file
pkill -f "stargazer_test_server.*--port ${PORT}" >/dev/null 2>&1 || true
pkill -x stargazer_test_server >/dev/null 2>&1 || true
exit 0
REMOTE_STOP

echo "[deploy] (4/4) Build + restart"
ssh "${REMOTE_HOST}" bash -s -- "${REMOTE_DIR}" "${PORT}" <<'REMOTE_BUILD'
set -euo pipefail
REMOTE_DIR="$1"
PORT="$2"

TRIPLET="x64-linux"

if [[ "$REMOTE_DIR" == ~* ]]; then
  REMOTE_DIR="${REMOTE_DIR/#\~/$HOME}"
fi

cd "$REMOTE_DIR"

VCPKG_ROOT="$REMOTE_DIR/third-party/vcpkg"
VCPKG_TOOLCHAIN="$VCPKG_ROOT/scripts/buildsystems/vcpkg.cmake"
VCPKG_INSTALLED_DIR="$VCPKG_ROOT/installed"

if [[ ! -f "$VCPKG_TOOLCHAIN" ]]; then
  if [[ -d "$VCPKG_ROOT/.git" ]]; then
    echo "[remote] vcpkg worktree looks broken; repairing (git reset/clean)"
    git -C "$VCPKG_ROOT" reset --hard HEAD
    git -C "$VCPKG_ROOT" clean -fdx
  fi
fi

if [[ ! -f "$VCPKG_TOOLCHAIN" ]]; then
  echo "[remote] ERROR: vcpkg toolchain missing: $VCPKG_TOOLCHAIN" >&2
  echo "[remote] Hint: ensure third-party/vcpkg is cloned/available" >&2
  exit 2
fi

if [[ ! -x "$VCPKG_ROOT/vcpkg" ]]; then
  echo "[remote] bootstrap vcpkg"
  "$VCPKG_ROOT/bootstrap-vcpkg.sh" -disableMetrics
fi

PROTOBUF_CFG_1="$VCPKG_INSTALLED_DIR/$TRIPLET/share/protobuf/protobuf-config.cmake"
PROTOBUF_CFG_2="$VCPKG_INSTALLED_DIR/$TRIPLET/share/protobuf/ProtobufConfig.cmake"
GRPC_CFG="$VCPKG_INSTALLED_DIR/$TRIPLET/share/grpc/gRPCConfig.cmake"
if [[ ! -f "$GRPC_CFG" || ( ! -f "$PROTOBUF_CFG_1" && ! -f "$PROTOBUF_CFG_2" ) ]]; then
  echo "[remote] vcpkg install deps (protobuf, grpc)"
  "$VCPKG_ROOT/vcpkg" install protobuf grpc --triplet "$TRIPLET"
fi

echo '[remote] cmake configure'
cmake -S . -B build \
  -DCMAKE_BUILD_TYPE=Release \
  -DCMAKE_TOOLCHAIN_FILE="$VCPKG_TOOLCHAIN" \
  -DVCPKG_TARGET_TRIPLET="$TRIPLET" \
  -DVCPKG_INSTALLED_DIR="$VCPKG_INSTALLED_DIR"

echo '[remote] cmake build'
cmake --build build -j

echo '[remote] start server'
nohup ./build/stargazer_test_server --host 0.0.0.0 --port "${PORT}" > server.log 2>&1 < /dev/null &
echo $! > server.pid

sleep 1
echo '[remote] status:'
ps -p "$(cat server.pid)" -o pid,etime,cmd

echo '[remote] pgrep:'
pgrep -a stargazer_test_server || true

if command -v ss >/dev/null 2>&1; then
  echo '[remote] listening:'
  ss -ltnp | grep -E ":${PORT}\\b" || true
fi

echo '[remote] tail server.log:'
tail -n 30 server.log || true
REMOTE_BUILD

echo "[deploy] Done."
