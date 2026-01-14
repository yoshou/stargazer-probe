using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Unity.Collections;

namespace StargazerProbe.Camera
{
    /// <summary>
    /// Manages camera capture using ARFoundation.
    /// 
    /// - Acquires camera images and intrinsic parameters from ARFoundation
    /// - Provides raw pixel data for encoding by other components
    /// - Captures at specified intervals
    /// </summary>
    public class ARFoundationCameraCapture : MonoBehaviour, ICameraCapture
    {
        // Serialized Fields - References
        [Header("AR Components")]
        [SerializeField] private ARCameraManager arCameraManager;

        // Serialized Fields - Settings
        [Header("Preview")]
        [Tooltip("Disable ARCameraBackground to prevent camera feed from appearing in background for UI preview")]
        [SerializeField] private bool disableARCameraBackground = true;
        
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

        // Private Fields - Conversion Buffer
        private NativeArray<byte> conversionBuffer;
        private int conversionBufferSize;
        
        // Private Fields - Camera Intrinsics
        private CameraIntrinsics currentIntrinsics;
        private bool hasIntrinsics;
        private float lastCpuImageErrorLogTime;
        
        private void Awake()
        {
            captureInterval = 1f / targetFPS;
            
            // Auto-search for ARCameraManager
            if (arCameraManager == null)
            {
                arCameraManager = FindAnyObjectByType<ARCameraManager>();
            }
        }

        private void Start()
        {
            // Disable background rendering (use UI RawImage for preview display)
            if (disableARCameraBackground)
            {
                if (arCameraManager != null)
                {
                    var bg = arCameraManager.GetComponent<ARCameraBackground>();
                    if (bg != null)
                    {
                        bg.enabled = false;
                    }
                }
            }
        }
        
        /// <summary>
        /// Initialize and start camera
        /// </summary>
        public void StartCapture()
        {
            Debug.Log("[ARFoundationCameraCapture] StartCapture called");
            
            if (IsCapturing)
            {
                Debug.LogWarning("[ARFoundationCameraCapture] Already capturing");
                return;
            }
            
            if (arCameraManager == null)
            {
                Debug.LogError("[ARFoundationCameraCapture] ARCameraManager is null - AR Foundation may not be properly set up in the scene");
                OnCaptureStartFailed?.Invoke("ARCameraManager not found");
                return;
            }
            
            Debug.Log($"[ARFoundationCameraCapture] ARCameraManager found, starting initialization");
            StartCoroutine(InitializeARCamera());
        }
        
        private IEnumerator InitializeARCamera()
        {
            // Wait for AR session to start
            yield return new WaitForSeconds(0.5f);
            
            // Select and set optimal resolution
            if (arCameraManager.subsystem != null)
            {
                using (var configs = arCameraManager.GetConfigurations(Allocator.Temp))
                {
                    if (configs.IsCreated && configs.Length > 0)
                    {
                        var bestConfig = configs[0];
                        int bestScore = int.MaxValue;

                        Debug.Log($"[ARFoundationCameraCapture] Selecting best config for target {targetWidth}x{targetHeight} @ {targetFPS}fps. Available configs:");

                        foreach (var config in configs)
                        {
                            int diffW = Mathf.Abs(config.width - targetWidth);
                            int diffH = Mathf.Abs(config.height - targetHeight);
                            int diffFPS = config.framerate.HasValue ? Mathf.Abs(config.framerate.Value - targetFPS) : 0;
                            
                            // Score calculation: prioritize resolution match, also consider FPS
                            // Large penalty for resolution mismatch
                            int score = (diffW * 10) + (diffH * 10) + diffFPS;

                            Debug.Log($" - {config.width}x{config.height} @ {config.framerate}fps (Score: {score})");

                            if (score < bestScore)
                            {
                                bestScore = score;
                                bestConfig = config;
                            }
                        }

                        Debug.Log($"[ARFoundationCameraCapture] Selected config: {bestConfig.width}x{bestConfig.height} @ {bestConfig.framerate}fps");
                        arCameraManager.currentConfiguration = bestConfig;
                    }
                }
            }

            // Register frame receive event
            arCameraManager.frameReceived += OnCameraFrameReceived;
            
            IsCapturing = true;
            lastCaptureTime = Time.unscaledTime;
            
            Debug.Log($"[ARFoundationCameraCapture] Started arCameraManager={arCameraManager != null} fps={targetFPS}");
            OnCaptureStarted?.Invoke();
        }
        
        private void OnCameraFrameReceived(ARCameraFrameEventArgs eventArgs)
        {
            if (!IsCapturing)
                return;
            
            // FPS calculation
            ActualFPS = 1f / Time.deltaTime;
            
            // Capture at specified interval
            if (Time.unscaledTime - lastCaptureTime >= captureInterval)
            {
                CaptureFrame(eventArgs);
                lastCaptureTime = Time.unscaledTime;
            }
        }
        
        private void CaptureFrame(ARCameraFrameEventArgs eventArgs)
        {
            try
            {
                // Get camera image
                if (!arCameraManager.TryAcquireLatestCpuImage(out XRCpuImage image))
                    return;
                
                try
                {
                    int w = image.width;
                    int h = image.height;

                    EnsurePixelBuffers(w, h);

                    Color32[] pixels = RentPixelBuffer();
                    if (pixels == null)
                    {
                        SkippedFrames++;
                        Debug.LogWarning("[ARFoundationCameraCapture] Pixel buffer unavailable. Skipping frame.");
                        return;
                    }

                    bool emitted = false;

                    try
                    {

                        // Get camera intrinsic parameters (do not reacquire CPU image)
                        UpdateIntrinsics(w, h);
                        
                        // Convert image data to Color32 array
                        var conversionParams = new XRCpuImage.ConversionParams
                        {
                            inputRect = new RectInt(0, 0, w, h),
                            outputDimensions = new Vector2Int(w, h),
                            outputFormat = TextureFormat.RGBA32,
                            transformation = XRCpuImage.Transformation.None
                        };
                        
                        int size = image.GetConvertedDataSize(conversionParams);
                        EnsureConversionBuffer(size);
                        
                        image.Convert(conversionParams, conversionBuffer);

                        var bufferAsColor = conversionBuffer.Reinterpret<Color32>(1);
                        NativeArray<Color32>.Copy(bufferAsColor, pixels);

                        // Update UI preview texture (main thread)
                        UpdatePreviewTexture(w, h, pixels);
                        
                        // Emit raw data via event
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
                catch (InvalidOperationException ex)
                {
                    // Can occur temporarily on some devices / right after startup, so throttle logs
                    const float logIntervalSeconds = 2f;
                    float now = Time.realtimeSinceStartup;
                    if (now - lastCpuImageErrorLogTime >= logIntervalSeconds)
                    {
                        lastCpuImageErrorLogTime = now;
                        Debug.LogWarning($"[ARFoundationCameraCapture] CpuImage not valid yet: {ex.Message}");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[ARFoundationCameraCapture] Image processing failed: {ex.GetType().Name}: {ex.Message}");
                }
                finally
                {
                    image.Dispose();
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ARFoundationCameraCapture] CaptureFrame failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private void UpdateIntrinsics(int imageWidth, int imageHeight)
        {
            if (arCameraManager == null)
            {
                hasIntrinsics = false;
                return;
            }

            // Get intrinsic parameters using ARFoundation standard API (do not reacquire CPU image)
            if (arCameraManager.TryGetIntrinsics(out XRCameraIntrinsics intrinsics))
            {
                currentIntrinsics = new CameraIntrinsics
                {
                    FocalLengthX = intrinsics.focalLength.x,
                    FocalLengthY = intrinsics.focalLength.y,
                    PrincipalPointX = intrinsics.principalPoint.x,
                    PrincipalPointY = intrinsics.principalPoint.y,
                    ImageWidth = imageWidth,
                    ImageHeight = imageHeight
                };

                hasIntrinsics = true;
                return;
            }

            currentIntrinsics = default;
            hasIntrinsics = false;
        }

        /// <summary>
        /// Stop camera
        /// </summary>
        public void StopCapture()
        {
            if (!IsCapturing)
                return;

            IsCapturing = false;

            if (arCameraManager != null)
            {
                arCameraManager.frameReceived -= OnCameraFrameReceived;
            }

            DisposeConversionBuffer();
            availableBuffers = null;

            Debug.Log("AR Camera stopped");
            OnCaptureStopped?.Invoke();
        }

        /// <summary>
        /// Change camera settings
        /// </summary>
        public void UpdateSettings(int newWidth, int newHeight, int newFPS, int newQuality)
        {
            // ARFoundation cannot change resolution, so update only FPS
            targetFPS = newFPS;
            captureInterval = 1f / targetFPS;

            Debug.Log($"AR Camera settings updated: {targetFPS}fps");
        }

        /// <summary>
        /// Get texture for preview
        /// </summary>
        public Texture GetPreviewTexture()
        {
            return previewTexture;
        }

        private void UpdatePreviewTexture(int width, int height, Color32[] pixels)
        {
            if (pixels == null || pixels.Length == 0)
                return;

            if (previewTexture == null || previewWidth != width || previewHeight != height)
            {
                previewWidth = width;
                previewHeight = height;

                if (previewTexture != null)
                    Destroy(previewTexture);

                previewTexture = new Texture2D(width, height, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
            }

            previewTexture.SetPixels32(pixels);
            previewTexture.Apply(false);
        }

        private void EnsurePixelBuffers(int w, int h)
        {
            if (availableBuffers == null || w != bufferWidth || h != bufferHeight)
            {
                RebuildPixelBuffers(w, h);
            }
        }

        private void RebuildPixelBuffers(int w, int h)
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

        private void EnsureConversionBuffer(int size)
        {
            if (!conversionBuffer.IsCreated || conversionBufferSize < size)
            {
                DisposeConversionBuffer();
                conversionBuffer = new NativeArray<byte>(size, Allocator.Persistent);
                conversionBufferSize = size;
            }
        }

        private void DisposeConversionBuffer()
        {
            if (conversionBuffer.IsCreated)
            {
                conversionBuffer.Dispose();
            }

            conversionBufferSize = 0;
        }

        private void OnDestroy()
        {
            StopCapture();
        }
    }
}
