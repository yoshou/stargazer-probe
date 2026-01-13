using System;
using UnityEngine;

namespace StargazerProbe.Camera
{
    /// <summary>
    /// Camera intrinsic parameters
    /// </summary>
    [Serializable]
    public struct CameraIntrinsics
    {
        public float FocalLengthX;
        public float FocalLengthY;
        public float PrincipalPointX;
        public float PrincipalPointY;
        public int ImageWidth;
        public int ImageHeight;
    }

    /// <summary>
    /// Raw camera frame data (before encoding)
    /// Data output directly from Capture
    /// </summary>
    [Serializable]
    public struct RawCameraFrameData
    {
        public double Timestamp;
        public int Width;
        public int Height;
        public Color32[] Pixels;
        public CameraIntrinsics Intrinsics;
        public System.Action<Color32[]> ReturnBufferCallback;  // For buffer pooling
    }

    /// <summary>
    /// Encoded camera frame data
    /// Data output from Encoder
    /// </summary>
    [Serializable]
    public struct CameraFrameData
    {
        public double Timestamp;
        public byte[] ImageData;
        public int Width;
        public int Height;
        public int Quality;
        public CameraIntrinsics Intrinsics;
    }
}
