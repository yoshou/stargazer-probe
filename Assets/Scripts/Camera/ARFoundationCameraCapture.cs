using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Unity.Collections;

namespace StargazerProbe.Camera
{
    /// <summary>
    /// Manages camera capture and JPEG compression using ARFoundation.
    /// 
    /// - Acquires camera images and intrinsic parameters from ARFoundation
    /// - JPEG encoding runs in background
    /// - Frames are skipped when encoding cannot keep up
    /// </summary>
    public class ARFoundationCameraCapture : MonoBehaviour, ICameraCapture
    {
        [Header("AR Components")]
        [SerializeField] private ARCameraManager arCameraManager;

        [Header("Preview")]
        [Tooltip("Disable ARCameraBackground to prevent camera feed from appearing in background for UI preview")]
        [SerializeField] private bool disableARCameraBackground = true;
        
        [Header("Camera Settings")]
        [SerializeField] private int targetWidth = 1280;
        [SerializeField] private int targetHeight = 720;
        [SerializeField] private int targetFPS = 30;
        
        [Header("JPEG Settings")]
        [SerializeField] private int jpegQuality = 75;
        [SerializeField] private bool autoAdjustQuality = true;
        
        [Header("Performance")]
        [SerializeField] private int maxSkipFrames = 3;
        [SerializeField] private int maxPendingEncodes = 2;
        
        // State
        public bool IsCapturing { get; private set; }
        public float ActualFPS { get; private set; }
        public int SkippedFrames { get; private set; }
        
        // Events
        public event Action<RawCameraFrameData> OnFrameCaptured;
        public event Action OnCaptureStarted;
        public event Action OnCaptureStopped;
        public event Action<string> OnCaptureStartFailed;
        
        // Internal variables
        private float captureInterval;
        private float lastCaptureTime;
        private int consecutiveSkips;
        private Texture2D previewTexture;

        private int previewWidth;
        private int previewHeight;
        
        // Camera intrinsic parameters
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
                consecutiveSkips = 0;
                
                // Get camera image
                if (!arCameraManager.TryAcquireLatestCpuImage(out XRCpuImage image))
                    return;
                
                try
                {
                    // Get camera intrinsic parameters (do not reacquire CPU image)
                    UpdateIntrinsics(image.width, image.height);
                    
                    // Convert image data to Color32 array
                    var conversionParams = new XRCpuImage.ConversionParams
                    {
                        inputRect = new RectInt(0, 0, image.width, image.height),
                        outputDimensions = new Vector2Int(image.width, image.height),
                        outputFormat = TextureFormat.RGBA32,
                        transformation = XRCpuImage.Transformation.None
                    };
                    
                    int size = image.GetConvertedDataSize(conversionParams);
                    var buffer = new NativeArray<byte>(size, Allocator.Temp);
                    
                    image.Convert(conversionParams, buffer);
                    
                    // Convert to Color32 array
                    Color32[] pixels = new Color32[image.width * image.height];

                    var pixelArray = new NativeArray<Color32>(image.width * image.height, Allocator.Temp);
                    try
                    {
                        var bufferAsColor = buffer.Reinterpret<Color32>(1);
                        NativeArray<Color32>.Copy(bufferAsColor, pixelArray);
                        pixelArray.CopyTo(pixels);
                    }
                    finally
                    {
                        pixelArray.Dispose();
                        buffer.Dispose();
                    }

                    // Update UI preview texture (main thread)
                    UpdatePreviewTexture(image.width, image.height, pixels);
                    
                    // Emit raw data via event
                    OnFrameCaptured?.Invoke(new RawCameraFrameData
                    {
                        Timestamp = Time.realtimeSinceStartup,
                        Width = image.width,
                        Height = image.height,
                        Pixels = pixels,
                        Intrinsics = hasIntrinsics ? currentIntrinsics : default,
                        ReturnBufferCallback = null  // ARFoundation has no buffer pooling
                    });
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

            Debug.Log("AR Camera stopped");
            OnCaptureStopped?.Invoke();
        }

        /// <summary>
        /// Change camera settings
        /// </summary>
        public void UpdateSettings(int newWidth, int newHeight, int newFPS, int newQuality)
        {
            // ARFoundation cannot change resolution, so update only FPS and quality
            targetFPS = newFPS;
            jpegQuality = Mathf.Clamp(newQuality, 1, 100);
            captureInterval = 1f / targetFPS;

            Debug.Log($"AR Camera settings updated: {targetFPS}fps, quality={jpegQuality}");
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

        private void OnDestroy()
        {
            StopCapture();
        }
    }
}
