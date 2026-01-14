#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

PROTO_DIR="${REPO_ROOT}/Assets/Scripts/Grpc/Proto"
GEN_DIR="${REPO_ROOT}/Assets/Scripts/Grpc/Generated"
PROTO_FILE="${PROTO_DIR}/sensor_stream.proto"

if [[ ! -f "${PROTO_FILE}" ]]; then
  echo "ERROR: Proto file not found: ${PROTO_FILE}" >&2
  exit 1
fi

echo "Generating C# code from ${PROTO_FILE}"

# Ensure output directory exists
mkdir -p "${GEN_DIR}"

# Use protoc from NuGet packages or system
PROTOC_PATH=""
GRPC_PLUGIN_PATH=""

# Try to find protoc from NuGet packages
NUGET_PACKAGES_DIR="${REPO_ROOT}/Assets/Packages"
if [[ -d "${NUGET_PACKAGES_DIR}" ]]; then
  PROTOC_PATH=$(find "${NUGET_PACKAGES_DIR}" -name "protoc.exe" -o -name "protoc" 2>/dev/null | head -n 1)
  GRPC_PLUGIN_PATH=$(find "${NUGET_PACKAGES_DIR}" -name "grpc_csharp_plugin.exe" -o -name "grpc_csharp_plugin" 2>/dev/null | head -n 1)
fi

# Fallback to system protoc
if [[ -z "${PROTOC_PATH}" ]]; then
  PROTOC_PATH=$(command -v protoc || echo "")
fi

if [[ -z "${PROTOC_PATH}" ]]; then
  echo "ERROR: protoc not found. Please install Protocol Buffers compiler." >&2
  echo "Install via: apt install protobuf-compiler (Linux) or brew install protobuf (Mac)" >&2
  exit 1
fi

if [[ -z "${GRPC_PLUGIN_PATH}" ]]; then
  GRPC_PLUGIN_PATH=$(command -v grpc_csharp_plugin || echo "")
fi

if [[ -z "${GRPC_PLUGIN_PATH}" ]]; then
  echo "WARNING: grpc_csharp_plugin not found. Only generating protobuf code, not gRPC stubs." >&2
fi

echo "Using protoc: ${PROTOC_PATH}"
echo "Output directory: ${GEN_DIR}"

# Generate protobuf C# code
"${PROTOC_PATH}" \
  --proto_path="${PROTO_DIR}" \
  --csharp_out="${GEN_DIR}" \
  "${PROTO_FILE}"

# Generate gRPC C# code if plugin is available
if [[ -n "${GRPC_PLUGIN_PATH}" ]]; then
  echo "Using grpc_csharp_plugin: ${GRPC_PLUGIN_PATH}"
  "${PROTOC_PATH}" \
    --proto_path="${PROTO_DIR}" \
    --grpc_out="${GEN_DIR}" \
    --plugin=protoc-gen-grpc="${GRPC_PLUGIN_PATH}" \
    "${PROTO_FILE}"
fi

echo "Done. Generated files in ${GEN_DIR}"
ls -lh "${GEN_DIR}"
