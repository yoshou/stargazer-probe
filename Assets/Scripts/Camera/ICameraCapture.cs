using System;
using UnityEngine;

namespace StargazerProbe.Camera
{
    /// <summary>
    /// Common interface for camera capture
    /// Used by both MobileCameraCapture and ARFoundationCameraCapture
    /// </summary>
    public interface ICameraCapture
    {
        // Properties
        bool IsCapturing { get; }
        float ActualFPS { get; }
        int SkippedFrames { get; }
        
        // Events
        event Action<RawCameraFrameData> OnFrameCaptured;
        event Action OnCaptureStarted;
        event Action OnCaptureStopped;
        event Action<string> OnCaptureStartFailed;
        
        // Methods
        void StartCapture();
        void StopCapture();
        void UpdateSettings(int newWidth, int newHeight, int newFPS, int newQuality);
        Texture GetPreviewTexture();
    }
}
