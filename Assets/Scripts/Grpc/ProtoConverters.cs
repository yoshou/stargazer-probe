using Google.Protobuf;
using UnityEngine;

namespace StargazerProbe.Grpc
{
    public static class ProtoConverters
    {
        public static Stargazer.Vector3D ToProtoVector3D(Vector3 v)
        {
            return new Stargazer.Vector3D { X = v.x, Y = v.y, Z = v.z };
        }

        public static Stargazer.Vector2D ToProtoVector2D(Vector2 v)
        {
            return new Stargazer.Vector2D { X = v.x, Y = v.y };
        }

        public static Stargazer.IntVector2D ToProtoIntVector2D(int x, int y)
        {
            return new Stargazer.IntVector2D { X = x, Y = y };
        }

        public static Stargazer.Inertial ToProtoInertial(StargazerProbe.Sensors.SensorData sensor)
        {
            return new Stargazer.Inertial
            {
                Acceleration = ToProtoVector3D(sensor.Acceleration),
                Gyroscope = ToProtoVector3D(sensor.Gyroscope),
                Magnetometer = ToProtoVector3D(sensor.Magnetometer),
                Gravity = ToProtoVector3D(sensor.Gravity)
            };
        }

        public static Stargazer.CameraImage ToProtoCameraImage(StargazerProbe.Camera.CameraFrameData frame)
        {
            var intrinsics = new Stargazer.CameraIntrinsics
            {
                FocalLength = ToProtoVector2D(new Vector2(frame.Intrinsics.FocalLengthX, frame.Intrinsics.FocalLengthY)),
                PrincipalPoint = ToProtoVector2D(new Vector2(frame.Intrinsics.PrincipalPointX, frame.Intrinsics.PrincipalPointY)),
                ImageSize = ToProtoIntVector2D(frame.Intrinsics.ImageWidth, frame.Intrinsics.ImageHeight),
                Distortion = new Stargazer.DistortionCoefficients
                {
                    K1 = 0f,
                    K2 = 0f,
                    P1 = 0f,
                    P2 = 0f,
                    K3 = 0f
                }
            };

            return new Stargazer.CameraImage
            {
                ImageData = ByteString.CopyFrom(frame.ImageData ?? System.Array.Empty<byte>()),
                ImageSize = ToProtoIntVector2D(frame.Width, frame.Height),
                Format = Stargazer.CameraImage.Types.ImageFormat.Jpeg,
                Intrinsics = intrinsics
            };
        }
    }
}
