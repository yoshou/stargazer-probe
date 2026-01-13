using System;
using UnityEngine;

namespace StargazerProbe.Camera
{
    /// <summary>
    /// カメラキャプチャの共通インターフェース
    /// MobileCameraCapture と ARFoundationCameraCapture の両方で使用
    /// </summary>
    public interface ICameraCapture
    {
        // プロパティ
        bool IsCapturing { get; }
        float ActualFPS { get; }
        int SkippedFrames { get; }
        
        // イベント
        event Action<RawCameraFrameData> OnFrameCaptured;
        event Action OnCaptureStarted;
        event Action OnCaptureStopped;
        event Action<string> OnCaptureStartFailed;
        
        // メソッド
        void StartCapture();
        void StopCapture();
        void UpdateSettings(int newWidth, int newHeight, int newFPS, int newQuality);
        Texture GetPreviewTexture();
    }
}
