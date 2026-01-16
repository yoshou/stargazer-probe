#include <atomic>
#include <chrono>
#include <cstdlib>
#include <iostream>
#include <memory>
#include <string>

#include <grpcpp/grpcpp.h>

#include "sensor.grpc.pb.h"

namespace {

class SensorServiceImpl final : public stargazer::Sensor::Service {
public:
  grpc::Status PublishCameraImage(
      grpc::ServerContext* context,
      grpc::ServerReader<stargazer::CameraImageMessage>* reader,
      google::protobuf::Empty* response) override {
    std::cout << "[PublishCameraImage] client connected. peer=" << (context ? context->peer() : "(null)") << std::endl;

    stargazer::CameraImageMessage msg;
    int32_t received = 0;

    while (reader->Read(&msg)) {
      ++received;

      const std::string& name = msg.name();
      const int64_t timestamp = msg.timestamp();
      const int num_images = msg.values_size();

      if ((received % 30) == 1) {
        std::cout << "[PublishCameraImage] received=" << received
                  << " name='" << name << "'"
                  << " timestamp=" << timestamp
                  << " images=" << num_images;

        if (num_images > 0) {
          const auto& img = msg.values(0);
          const int image_bytes = static_cast<int>(img.image_data().size());
          std::cout << " image_bytes=" << image_bytes;

          if (img.has_intrinsics()) {
            const auto& intr = img.intrinsics();
            std::cout << " intrinsics={fx=" << intr.focal_length().x()
                      << ", fy=" << intr.focal_length().y()
                      << ", cx=" << intr.principal_point().x()
                      << ", cy=" << intr.principal_point().y()
                      << ", w=" << intr.image_size().x()
                      << ", h=" << intr.image_size().y()
                      << "}";
            if (intr.has_distortion()) {
              const auto& dist = intr.distortion();
              std::cout << " distortion={k1=" << dist.k1()
                        << ", k2=" << dist.k2()
                        << ", p1=" << dist.p1()
                        << ", p2=" << dist.p2()
                        << ", k3=" << dist.k3()
                        << "}";
            }
          }
        }

        std::cout << std::endl;
      }
    }

    std::cout << "[PublishCameraImage] stream ended. total_received=" << received << std::endl;
    return grpc::Status::OK;
  }

  grpc::Status PublishInertial(
      grpc::ServerContext* context,
      grpc::ServerReader<stargazer::InertialMessage>* reader,
      google::protobuf::Empty* response) override {
    std::cout << "[PublishInertial] client connected. peer=" << (context ? context->peer() : "(null)") << std::endl;

    stargazer::InertialMessage msg;
    int32_t received = 0;

    while (reader->Read(&msg)) {
      ++received;

      if ((received % 100) == 1) {
        std::cout << "[PublishInertial] received=" << received
                  << " name='" << msg.name() << "'"
                  << " timestamp=" << msg.timestamp()
                  << " samples=" << msg.values_size()
                  << std::endl;
      }
    }

    std::cout << "[PublishInertial] stream ended. total_received=" << received << std::endl;
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

  SensorServiceImpl service;

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
