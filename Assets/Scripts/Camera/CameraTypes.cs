using System;
using UnityEngine;

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
    /// 生のカメラフレームデータ（エンコード前）
    /// Captureから直接出力されるデータ
    /// </summary>
    [Serializable]
    public struct RawCameraFrameData
    {
        public double Timestamp;
        public int Width;
        public int Height;
        public Color32[] Pixels;
        public CameraIntrinsics Intrinsics;
        public System.Action<Color32[]> ReturnBufferCallback;  // バッファプーリング用
    }

    /// <summary>
    /// エンコード済みカメラフレームデータ
    /// Encoderから出力されるデータ
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
