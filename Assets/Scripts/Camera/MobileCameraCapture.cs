using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace StargazerProbe.Camera
{
    /// <summary>
    /// Manages mobile camera capture using WebCamTexture.
    /// 
    /// - Pixel acquisition on main thread (WebCamTexture constraint)
    /// - Provides raw pixel data with buffer pooling for encoding by other components
    /// - Handles screen rotation and resolution changes
    /// </summary>
    public class MobileCameraCapture : MonoBehaviour, ICameraCapture
    {
        // Serialized Fields - Settings
        [Header("Camera Settings")]
        [SerializeField] private int width = 1280;
        [SerializeField] private int height = 720;
        [SerializeField] private int targetFPS = 30;
        
        [Header("Performance")]
        [Tooltip("Number of pixel buffers for encoding. More reduces GC but uses more memory")]
        [SerializeField] private int pixelBufferCount = 3;
        
        // Public Properties - State
        public bool IsCapturing { get; private set; }
        public float ActualFPS { get; private set; }
        public int SkippedFrames { get; private set; }
        
        // Events
        public event Action<RawCameraFrameData> OnFrameCaptured;
        public event Action OnCaptureStarted;
        public event Action OnCaptureStopped;
        public event Action<string> OnCaptureStartFailed;
        
        // Private Fields - Camera
        private WebCamTexture webCamTexture;
        private int bufferWidth;
        private int bufferHeight;
        private readonly object bufferLock = new object();
        private Queue<Color32[]> availableBuffers;
        
        // Private Fields - Capture State
        private float captureInterval;
        private float lastCaptureTime;
        
        private void Awake()
        {
            captureInterval = 1f / targetFPS;
        }
        
        /// <summary>
        /// Initialize and start camera
        /// </summary>
        public void StartCapture()
        {
            if (IsCapturing)
            {
                Debug.LogWarning("Camera is already capturing");
                return;
            }
            
            StartCoroutine(InitializeCamera());
        }
        
        private IEnumerator InitializeCamera()
        {
            // Get camera device
            WebCamDevice[] devices = WebCamTexture.devices;
            if (devices.Length == 0)
            {
                Debug.LogError("No camera devices found");
                OnCaptureStartFailed?.Invoke("No camera devices found");
                yield break;
            }
            
            // Prefer rear camera
            string deviceName = devices[0].name;
            foreach (var device in devices)
            {
                if (!device.isFrontFacing)
                {
                    deviceName = device.name;
                    break;
                }
            }
            
            Debug.Log($"Using camera: {deviceName}");
            
            // Initialize WebCamTexture
            webCamTexture = new WebCamTexture(deviceName, width, height, targetFPS);
            webCamTexture.Play();

            // Wait for camera to start and resolution to be determined
            const float timeoutSeconds = 3f;
            float startWait = Time.realtimeSinceStartup;
            while (webCamTexture != null && webCamTexture.isPlaying && (webCamTexture.width <= 16 || webCamTexture.height <= 16))
            {
                if (Time.realtimeSinceStartup - startWait > timeoutSeconds)
                    break;
                yield return null;
            }

            if (webCamTexture == null || !webCamTexture.isPlaying)
            {
                Debug.LogError("Failed to start camera");
                OnCaptureStartFailed?.Invoke("Failed to start camera");
                yield break;
            }
            
            // Create capture texture
            int w = webCamTexture.width;
            int h = webCamTexture.height;

            bufferWidth = w;
            bufferHeight = h;

            int buffers = Mathf.Max(2, pixelBufferCount);
            availableBuffers = new Queue<Color32[]>(buffers);
            for (int i = 0; i < buffers; i++)
            {
                availableBuffers.Enqueue(new Color32[w * h]);
            }
            
            IsCapturing = true;
            lastCaptureTime = Time.unscaledTime;
            
            Debug.Log($"[MobileCameraCapture] Started resolution={webCamTexture.width}x{webCamTexture.height} fps={targetFPS}");
            OnCaptureStarted?.Invoke();
        }
        
        private void Update()
        {
            if (!IsCapturing || webCamTexture == null || !webCamTexture.isPlaying)
                return;

            // Screen rotation can change camera's actual resolution.
            // If buffer is old, internal allocation will occur or data will be corrupted, so rebuild.
            // (This reconstruction: stop encode loop → drain queue → rebuild buffers.)
            int w = webCamTexture.width;
            int h = webCamTexture.height;
            if (w > 16 && h > 16 && (w != bufferWidth || h != bufferHeight))
            {
                RebuildPixelBuffers(w, h);
            }
            
            // FPS calculation
            ActualFPS = 1f / Time.deltaTime;
            
            // Capture at specified interval
            if (Time.unscaledTime - lastCaptureTime >= captureInterval)
            {
                CaptureFrame();
                lastCaptureTime = Time.unscaledTime;
            }
        }

        private void RebuildPixelBuffers(int w, int h)
        {
            Debug.Log($"Camera resolution changed: {bufferWidth}x{bufferHeight} -> {w}x{h}. Rebuilding buffers.");

            bufferWidth = w;
            bufferHeight = h;

            int buffers = Mathf.Max(2, pixelBufferCount);
            availableBuffers = new Queue<Color32[]>(buffers);
            for (int i = 0; i < buffers; i++)
            {
                availableBuffers.Enqueue(new Color32[w * h]);
            }
        }
        
        private void CaptureFrame()
        {
            Color32[] buffer = RentPixelBuffer();
            if (buffer == null)
            {
                SkippedFrames++;
                return;
            }

            // Pixel acquisition from WebCamTexture can only be safely called on main thread.
            buffer = webCamTexture.GetPixels32(buffer);

            int w = webCamTexture.width;
            int h = webCamTexture.height;

            // Emit raw data via event
            OnFrameCaptured?.Invoke(new RawCameraFrameData
            {
                Timestamp = Time.realtimeSinceStartup,
                Width = w,
                Height = h,
                Pixels = buffer,
                Intrinsics = default,  // MobileCameraCapture does not acquire intrinsic parameters
                ReturnBufferCallback = ReturnPixelBuffer
            });
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

            // Do not reuse buffers with size mismatch due to resolution change.
            int expected = bufferWidth > 0 && bufferHeight > 0 ? bufferWidth * bufferHeight : buffer.Length;
            if (buffer.Length != expected)
                return;

            lock (bufferLock)
            {
                availableBuffers.Enqueue(buffer);
            }
        }
        
        /// <summary>
        /// Stop camera
        /// </summary>
        public void StopCapture()
        {
            if (!IsCapturing)
                return;
            
            IsCapturing = false;
            
            if (webCamTexture != null)
            {
                webCamTexture.Stop();
                Destroy(webCamTexture);
                webCamTexture = null;
            }
            
            Debug.Log("Camera stopped");
            OnCaptureStopped?.Invoke();
        }
        
        /// <summary>
        /// Change camera settings
        /// </summary>
        public void UpdateSettings(int newWidth, int newHeight, int newFPS, int newQuality)
        {
            bool wasCapturing = IsCapturing;
            
            if (wasCapturing)
            {
                StopCapture();
            }
            
            width = newWidth;
            height = newHeight;
            targetFPS = newFPS;
            captureInterval = 1f / targetFPS;
            
            if (wasCapturing)
            {
                StartCapture();
            }
        }
        
        /// <summary>
        /// Get texture for preview
        /// </summary>
        public Texture GetPreviewTexture()
        {
            return webCamTexture;
        }
        
        private void OnDestroy()
        {
            StopCapture();
        }
    }
}
