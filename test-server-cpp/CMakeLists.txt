cmake_minimum_required(VERSION 3.20)
project(stargazer_probe_test_server LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# Find gRPC and Protobuf via vcpkg (CONFIG mode)
find_package(Protobuf CONFIG REQUIRED)
find_package(gRPC CONFIG REQUIRED)

set(PROTO_DIR "${CMAKE_CURRENT_SOURCE_DIR}/proto")
set(GEN_DIR "${CMAKE_CURRENT_BINARY_DIR}/generated")
file(MAKE_DIRECTORY "${GEN_DIR}")

set(PROTO_FILE "${PROTO_DIR}/sensor_stream.proto")

set(PROTO_SRC "${GEN_DIR}/sensor_stream.pb.cc")
set(PROTO_HDR "${GEN_DIR}/sensor_stream.pb.h")
set(GRPC_SRC "${GEN_DIR}/sensor_stream.grpc.pb.cc")
set(GRPC_HDR "${GEN_DIR}/sensor_stream.grpc.pb.h")

add_custom_command(
  OUTPUT
    "${PROTO_SRC}" "${PROTO_HDR}" "${GRPC_SRC}" "${GRPC_HDR}"
  COMMAND $<TARGET_FILE:protobuf::protoc>
    --proto_path=${PROTO_DIR}
    --cpp_out=${GEN_DIR}
    --grpc_out=${GEN_DIR}
    --plugin=protoc-gen-grpc=$<TARGET_FILE:gRPC::grpc_cpp_plugin>
    ${PROTO_FILE}
  DEPENDS "${PROTO_FILE}" protobuf::protoc gRPC::grpc_cpp_plugin
  COMMENT "Generating C++ sources from sensor_stream.proto"
  WORKING_DIRECTORY "${CMAKE_CURRENT_SOURCE_DIR}"
)

add_executable(stargazer_test_server
  src/main.cc
  "${PROTO_SRC}"
  "${GRPC_SRC}"
)

target_include_directories(stargazer_test_server PRIVATE
  "${GEN_DIR}"
)

target_link_libraries(stargazer_test_server PRIVATE
  gRPC::grpc++
  protobuf::libprotobuf
)
