#include <atomic>
#include <chrono>
#include <cstdlib>
#include <iostream>
#include <memory>
#include <string>

#include <grpcpp/grpcpp.h>

#include "sensor_stream.grpc.pb.h"

namespace {

class SensorStreamServiceImpl final : public stargazer::SensorStream::Service {
public:
  grpc::Status StreamData(
      grpc::ServerContext* context,
      grpc::ServerReaderWriter<stargazer::DataResponse, stargazer::DataPacket>* stream) override {
    std::cout << "[StreamData] client connected. peer=" << (context ? context->peer() : "(null)") << std::endl;

    stargazer::DataPacket packet;
    int32_t received = 0;

    while (stream->Read(&packet)) {
      ++received;

      // Minimal logging (avoid printing image bytes)
      const std::string& device_id = packet.device_id();
      const int image_bytes = packet.has_camera() ? static_cast<int>(packet.camera().image_data().size()) : 0;

      if ((received % 30) == 1) {
        std::cout << "[StreamData] received=" << received
                  << " device_id='" << device_id << "'"
                  << " ts=" << packet.timestamp()
                  << " image_bytes=" << image_bytes
                  ;

        if (packet.has_camera() && packet.camera().has_intrinsics()) {
          const auto& c = packet.camera();
          std::cout << " intrinsics={fx=" << c.focal_length_x()
                    << ", fy=" << c.focal_length_y()
                    << ", cx=" << c.principal_point_x()
                    << ", cy=" << c.principal_point_y()
                    << ", w=" << c.intrinsics_image_width()
                    << ", h=" << c.intrinsics_image_height()
                    << "}";
        }

        std::cout << std::endl;
      }

      stargazer::DataResponse resp;
      resp.set_success(true);
      resp.set_received_packets(received);
      resp.set_message("ok");

      // Echo a response for each packet (bidi stream)
      if (!stream->Write(resp)) {
        break;
      }
    }

    std::cout << "[StreamData] stream ended. total_received=" << received << std::endl;
    return grpc::Status::OK;
  }
};

std::string GetArg(int argc, char** argv, const std::string& key, const std::string& fallback) {
  for (int i = 1; i < argc - 1; ++i) {
    if (argv[i] == key) {
      return argv[i + 1];
    }
  }
  return fallback;
}

} // namespace

int main(int argc, char** argv) {
  const std::string host = GetArg(argc, argv, "--host", "0.0.0.0");
  const std::string port = GetArg(argc, argv, "--port", "50051");
  const std::string address = host + ":" + port;

  SensorStreamServiceImpl service;

  grpc::ServerBuilder builder;
  builder.AddListeningPort(address, grpc::InsecureServerCredentials());
  builder.RegisterService(&service);

  std::unique_ptr<grpc::Server> server(builder.BuildAndStart());
  if (!server) {
    std::cerr << "Failed to start server on " << address << std::endl;
    return 1;
  }

  std::cout << "Stargazer Probe test gRPC server listening on " << address << std::endl;
  server->Wait();

  return 0;
}
