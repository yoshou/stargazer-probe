using System;

namespace StargazerProbe.Camera
{
    /// <summary>
    /// カメラ内部パラメータ
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
    /// カメラフレームデータ
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
