using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_ANDROID
using UnityEngine.Android;
#endif

namespace StargazerProbe.Camera
{
    /// <summary>
    /// Camera capture implementation backed by Android Camera2 API.
    ///
    /// - Uses a small Android(Java) plugin to access Camera2 and provide the latest frame as JPEG bytes.
    /// - Unity side decodes JPEG into Texture2D, copies pixels into pooled Color32 buffers, and emits RawCameraFrameData.
    /// - Preview rotation/mirroring is handled on UI side (similar to WebCamTexture).
    /// </summary>
    public sealed class Camera2CameraCapture : MonoBehaviour, ICameraCapture
    {
        [Header("Camera Settings")]
        [SerializeField] private int targetWidth = 1280;
        [SerializeField] private int targetHeight = 720;
        [SerializeField] private int targetFPS = 30;

        [Header("Performance")]
        [Tooltip("Number of pixel buffers for encoding. More reduces GC but uses more memory")]
        [SerializeField] private int pixelBufferCount = 8;

        // Public Properties - State
        public bool IsCapturing { get; private set; }
        public float ActualFPS { get; private set; }
        public int SkippedFrames { get; private set; }

        // For UI preview adjustments
        public int RotationDegrees { get; private set; }
        public bool IsMirrored { get; private set; }

        // Events
        public event Action<RawCameraFrameData> OnFrameCaptured;
        public event Action OnCaptureStarted;
        public event Action OnCaptureStopped;
        public event Action<string> OnCaptureStartFailed;

        // Private Fields - Capture State
        private float captureInterval;
        private float lastCaptureTime;

        // Private Fields - Preview
        private Texture2D previewTexture;
        private int previewWidth;
        private int previewHeight;

        // Private Fields - Buffer Pool
        private int bufferWidth;
        private int bufferHeight;
        private readonly object bufferLock = new object();
        private Queue<Color32[]> availableBuffers;

        // Private Fields - Intrinsics
        private CameraIntrinsics currentIntrinsics;
        private bool hasIntrinsics;

#if UNITY_ANDROID && !UNITY_EDITOR
        private AndroidJavaObject camera2;
        private AndroidJavaObject activity;
#endif

        private void Awake()
        {
            captureInterval = 1f / Mathf.Max(1, targetFPS);
        }

        public void StartCapture()
        {
            if (IsCapturing)
                return;

#if UNITY_ANDROID && !UNITY_EDITOR
            StartCoroutine(InitializeCamera2());
#else
            OnCaptureStartFailed?.Invoke("Camera2 capture is supported only on Android device builds");
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private IEnumerator InitializeCamera2()
        {
            // Request camera permission if needed
            if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
            {
                Permission.RequestUserPermission(Permission.Camera);

                const float timeoutSeconds = 8f;
                float start = Time.realtimeSinceStartup;
                while (!Permission.HasUserAuthorizedPermission(Permission.Camera) && Time.realtimeSinceStartup - start < timeoutSeconds)
                {
                    yield return null;
                }
            }

            if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
            {
                OnCaptureStartFailed?.Invoke("Camera permission denied");
                yield break;
            }

            try
            {
                activity = new AndroidJavaClass("com.unity3d.player.UnityPlayer")
                    .GetStatic<AndroidJavaObject>("currentActivity");

                camera2 = new AndroidJavaObject("com.stargazerprobe.camera.Camera2Capture");
                bool ok = camera2.Call<bool>(
                    "start",
                    activity,
                    targetWidth,
                    targetHeight,
                    targetFPS,
                    false // useFront
                );

                if (!ok)
                {
                    OnCaptureStartFailed?.Invoke("Camera2 start failed");
                    yield break;
                }

                UpdateOrientationFlags();
                UpdateIntrinsicsFromJava();

                IsCapturing = true;
                lastCaptureTime = Time.unscaledTime;
                OnCaptureStarted?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Camera2CameraCapture] Initialize failed: {ex.GetType().Name}: {ex.Message}");
                OnCaptureStartFailed?.Invoke(ex.Message);
                yield break;
            }
        }
#endif

        private void Update()
        {
            if (!IsCapturing)
                return;

            ActualFPS = 1f / Mathf.Max(0.0001f, Time.deltaTime);

            if (Time.unscaledTime - lastCaptureTime < captureInterval)
                return;

            lastCaptureTime = Time.unscaledTime;
            CaptureFrame();
        }

        private void CaptureFrame()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (camera2 == null)
                return;

            try
            {
                UpdateOrientationFlags();

                byte[] jpeg = camera2.Call<byte[]>("consumeLatestJpeg");
                if (jpeg == null || jpeg.Length == 0)
                    return;

                EnsurePreviewTexture();

                // Decode into Texture2D (RGBA32)
                if (!ImageConversion.LoadImage(previewTexture, jpeg, markNonReadable: false))
                    return;

                int w = previewTexture.width;
                int h = previewTexture.height;

                EnsurePixelBuffers(w, h);

                Color32[] pixels = RentPixelBuffer();
                if (pixels == null)
                {
                    SkippedFrames++;
                    return;
                }

                bool emitted = false;
                try
                {
                    // Copy decoded pixels into pooled buffer
                    var raw = previewTexture.GetRawTextureData<Color32>();
                    if (raw.Length == pixels.Length)
                    {
                        raw.CopyTo(pixels);
                    }
                    else
                    {
                        // Fallback (should be rare): allocate and copy
                        Color32[] tmp = previewTexture.GetPixels32();
                        int count = Mathf.Min(tmp.Length, pixels.Length);
                        Array.Copy(tmp, pixels, count);
                    }

                    // Keep intrinsics aligned to current image dimensions
                    if (hasIntrinsics)
                    {
                        currentIntrinsics.ImageWidth = w;
                        currentIntrinsics.ImageHeight = h;
                    }

                    OnFrameCaptured?.Invoke(new RawCameraFrameData
                    {
                        Timestamp = Time.realtimeSinceStartup,
                        Width = w,
                        Height = h,
                        Pixels = pixels,
                        Intrinsics = hasIntrinsics ? currentIntrinsics : default,
                        ReturnBufferCallback = ReturnPixelBuffer
                    });

                    emitted = true;
                }
                finally
                {
                    if (!emitted)
                    {
                        ReturnPixelBuffer(pixels);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Camera2CameraCapture] CaptureFrame failed: {ex.GetType().Name}: {ex.Message}");
            }
#endif
        }

        public void StopCapture()
        {
            if (!IsCapturing)
                return;

            IsCapturing = false;

#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                camera2?.Call("stop");
            }
            catch { }

            try
            {
                camera2?.Dispose();
            }
            catch { }

            camera2 = null;
            activity = null;
#endif

            availableBuffers = null;
            hasIntrinsics = false;

            if (previewTexture != null)
            {
                Destroy(previewTexture);
                previewTexture = null;
            }

            OnCaptureStopped?.Invoke();
        }

        public void UpdateSettings(int newWidth, int newHeight, int newFPS, int newQuality)
        {
            bool wasCapturing = IsCapturing;
            if (wasCapturing)
            {
                StopCapture();
            }

            targetWidth = newWidth;
            targetHeight = newHeight;
            targetFPS = newFPS;
            captureInterval = 1f / Mathf.Max(1, targetFPS);

            if (wasCapturing)
            {
                StartCapture();
            }
        }

        public Texture GetPreviewTexture()
        {
            return previewTexture;
        }

        private void EnsurePreviewTexture()
        {
            if (previewTexture == null)
            {
                // Dummy initial size; LoadImage will resize on first decode.
                previewTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };

                previewWidth = previewTexture.width;
                previewHeight = previewTexture.height;
            }
        }

        private void EnsurePixelBuffers(int w, int h)
        {
            if (availableBuffers == null || w != bufferWidth || h != bufferHeight)
            {
                bufferWidth = w;
                bufferHeight = h;

                int buffers = Mathf.Max(2, pixelBufferCount);
                availableBuffers = new Queue<Color32[]>(buffers);
                for (int i = 0; i < buffers; i++)
                {
                    availableBuffers.Enqueue(new Color32[w * h]);
                }
            }
        }

        private Color32[] RentPixelBuffer()
        {
            if (availableBuffers == null)
                return null;

            lock (bufferLock)
            {
                if (availableBuffers.Count == 0)
                    return null;
                return availableBuffers.Dequeue();
            }
        }

        private void ReturnPixelBuffer(Color32[] buffer)
        {
            if (buffer == null || availableBuffers == null)
                return;

            int expected = bufferWidth > 0 && bufferHeight > 0 ? bufferWidth * bufferHeight : buffer.Length;
            if (buffer.Length != expected)
                return;

            lock (bufferLock)
            {
                availableBuffers.Enqueue(buffer);
            }
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private void UpdateOrientationFlags()
        {
            if (camera2 == null)
                return;

            try
            {
                RotationDegrees = camera2.Call<int>("getRotationDegrees", activity);
                IsMirrored = camera2.Call<bool>("getIsFrontFacing");
            }
            catch
            {
                // Keep last known values
            }
        }

        private void UpdateIntrinsicsFromJava()
        {
            if (camera2 == null)
            {
                hasIntrinsics = false;
                return;
            }

            try
            {
                float[] intr = camera2.Call<float[]>("getIntrinsics");
                if (intr == null || intr.Length < 4)
                {
                    hasIntrinsics = false;
                    return;
                }

                currentIntrinsics = new CameraIntrinsics
                {
                    FocalLengthX = intr[0],
                    FocalLengthY = intr[1],
                    PrincipalPointX = intr[2],
                    PrincipalPointY = intr[3],
                    ImageWidth = 0,
                    ImageHeight = 0
                };
                hasIntrinsics = true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Camera2CameraCapture] Intrinsics unavailable: {ex.Message}");
                hasIntrinsics = false;
            }
        }
#endif

        private void OnDestroy()
        {
            StopCapture();
        }
    }
}
