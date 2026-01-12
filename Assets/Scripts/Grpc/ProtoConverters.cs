using Google.Protobuf;
using UnityEngine;

namespace StargazerProbe.Grpc
{
    public static class ProtoConverters
    {
        public static Stargazer.Vector3 ToProto(Vector3 v)
        {
            return new Stargazer.Vector3 { X = v.x, Y = v.y, Z = v.z };
        }

        public static Stargazer.SensorData ToProto(StargazerProbe.Sensors.SensorData sensor)
        {
            return new Stargazer.SensorData
            {
                Acceleration = ToProto(sensor.Acceleration),
                Gyroscope = ToProto(sensor.Gyroscope),
                Magnetometer = ToProto(sensor.Magnetometer),
                Gravity = ToProto(sensor.Gravity)
            };
        }

        public static Stargazer.CameraFrame ToProto(StargazerProbe.Camera.CameraFrameData frame)
        {
            return new Stargazer.CameraFrame
            {
                ImageData = ByteString.CopyFrom(frame.ImageData ?? System.Array.Empty<byte>()),
                Width = frame.Width,
                Height = frame.Height,
                Quality = frame.Quality,
                CameraTimestamp = frame.Timestamp
            };
        }
    }
}
