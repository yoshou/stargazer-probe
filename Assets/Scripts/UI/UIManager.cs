using UnityEngine;
using UnityEngine.UI;
using TMPro;
using StargazerProbe.Sensors;
using StargazerProbe.Camera;
using StargazerProbe.Config;
using StargazerProbe.Grpc;
using System;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace StargazerProbe.UI
{
    /// <summary>
    /// Manages the main UI
    /// </summary>
    public class UIManager : MonoBehaviour
    {
        // Serialized Fields - References
        [Header("References")]
        [SerializeField] private IMUSensorManager sensorManager;
        [SerializeField] private GrpcDataStreamer grpcDataStreamer;
        
        [Header("Encoder Settings")]
        [SerializeField] private int jpegQuality = 75;
        
        [Header("UI - Camera Preview")]
        [SerializeField] private RawImage cameraPreviewImage;
        
        [Header("UI - Status Bar")]
        [SerializeField] private TextMeshProUGUI connectionStatusText;
        [SerializeField] private TextMeshProUGUI fpsCounterText;
        [SerializeField] private TextMeshProUGUI queueSizeText;
        
        [Header("UI - Sensor Display")]
        [SerializeField] private TextMeshProUGUI accelText;
        [SerializeField] private TextMeshProUGUI gyroText;
        [SerializeField] private TextMeshProUGUI magText;
        
        [Header("UI - Control Panel")]
        [SerializeField] private Button settingsButton;
        [SerializeField] private Button startStopButton;
        [SerializeField] private TextMeshProUGUI startStopButtonText;
        [SerializeField] private Image recordingIndicator;
        
        [Header("UI - Settings Panel")]
        [SerializeField] private GameObject settingsPanel;
        
        // Private Fields - Runtime References
        private ICameraCapture cameraCapture;  // Created by factory
        private CameraFrameEncoder frameEncoder;  // Created and managed by UIManager
        private SystemConfig config;
        
        // Private Fields - State
        private bool isRunning = false;
        private Coroutine connectionMonitorCoroutine;
        private ConnectionState lastConnectionState = ConnectionState.Disconnected;
        
        // Private Fields - FPS Tracking
        private const float fpsUpdateInterval = 0.5f;
        private float lastFpsUpdate;
        private int frameCount;
        
        private void Start()
        {
            config = SystemConfig.Instance;

            // Reduce Android logcat noise: remove stack traces for normal logs/warnings.
            // (Those extra "Namespace.Class:Method" lines come from Unity stack trace settings.)
            Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
            Application.SetStackTraceLogType(LogType.Warning, StackTraceLogType.None);
            Application.SetStackTraceLogType(LogType.Error, StackTraceLogType.ScriptOnly);

            // Create camera capture using factory
            cameraCapture = CameraCaptureFactory.CreateCameraCapture(gameObject);
            if (cameraCapture == null)
            {
                Debug.LogError("Failed to create camera capture");
            }
            else
            {
                // Create CameraFrameEncoder
                frameEncoder = new CameraFrameEncoder(System.Threading.SynchronizationContext.Current);
                frameEncoder.Start();
                
                // Set up callback chain: Capture → Encoder → Grpc
                
                // 1. Pass raw data from Capture to Encoder
                cameraCapture.OnFrameCaptured += (rawData) =>
                {
                    // Enqueue raw data to encoder
                    frameEncoder.TryEnqueue(
                        rawData.Timestamp,
                        rawData.Width,
                        rawData.Height,
                        jpegQuality,
                        rawData.Pixels,
                        rawData.Intrinsics,
                        rawData.ReturnBufferCallback);
                };
                
                // 2. Pass encoded data from Encoder to Grpc
                frameEncoder.OnFrameEncoded += (frameData) =>
                {
                    // Callback for UI preview
                    OnFrameCaptured(frameData);
                    
                    // Send to Grpc
                    if (grpcDataStreamer != null)
                    {
                        grpcDataStreamer.SendFrameData(frameData);
                    }
                };
            }

            if (grpcDataStreamer == null)
            {
                grpcDataStreamer = GetComponent<GrpcDataStreamer>();
                if (grpcDataStreamer == null)
                {
                    grpcDataStreamer = FindAnyObjectByType<GrpcDataStreamer>();
                }

                // If not found in scene, add it to this GameObject so gRPC is never silently disabled.
                if (grpcDataStreamer == null)
                {
                    grpcDataStreamer = gameObject.AddComponent<GrpcDataStreamer>();
                }
            }

            InitializeUI();
            SetupEventListeners();

            // Start camera capture immediately after app start (for preview display)
            if (cameraCapture != null)
            {
                cameraCapture.StartCapture();
            }
        }
        
        private void InitializeUI()
        {
            // Initial state
            if (settingsPanel != null)
            {
                settingsPanel.SetActive(false);
            }
            if (recordingIndicator != null)
            {
                recordingIndicator.enabled = false;
            }
            UpdateConnectionStatus(ConnectionState.Disconnected);
            UpdateStartStopButton(false);
        }
        
        private void SetupEventListeners()
        {
            // Button events
            if (startStopButton != null)
            {
                startStopButton.onClick.AddListener(OnStartStopButtonClicked);
            }
            else
            {
                Debug.LogError("ERROR: StartStopButton is null!");
            }
            
            if (settingsButton != null)
            {
                settingsButton.onClick.AddListener(OnSettingsButtonClicked);
            }
            else
            {
                Debug.LogError("ERROR: SettingsButton is null!");
            }
            
            // Sensor events
            if (sensorManager != null)
            {
                sensorManager.OnSensorDataUpdated += OnSensorDataUpdated;
            }
        }
        
        private void Update()
        {
            UpdateFPS();
            UpdateCameraPreview();
        }
        
        private void UpdateFPS()
        {
            frameCount++;
            
            if (Time.time - lastFpsUpdate >= fpsUpdateInterval)
            {
                float fps = frameCount / (Time.time - lastFpsUpdate);
                if (fpsCounterText != null)
                {
                    fpsCounterText.text = $"FPS: {fps:F1}";
                }

                UpdatePipelineStats();
                
                frameCount = 0;
                lastFpsUpdate = Time.time;
            }
        }

        private void UpdatePipelineStats()
        {
            if (queueSizeText == null)
                return;

            // Keep display short to avoid layout issues.
            // Drop shows "number of times packets were discarded due to send queue overflow".
            // This is separate from network disconnection/send errors.
            int dropped = grpcDataStreamer != null ? grpcDataStreamer.FramesDroppedQueueOverflow : 0;
            queueSizeText.text = $"Drop: {dropped}";
        }
        
        private void UpdateCameraPreview()
        {
            if (cameraCapture == null || !cameraCapture.IsCapturing || cameraPreviewImage == null)
            {
                return;
            }

            Texture previewTexture = cameraCapture.GetPreviewTexture();
            if (previewTexture == null)
            {
                return;
            }

            if (cameraPreviewImage.texture != previewTexture)
            {
                cameraPreviewImage.texture = previewTexture;
                cameraPreviewImage.material = null;
                cameraPreviewImage.color = Color.white;
            }

            if (previewTexture is WebCamTexture webCam)
            {
                var fitter = cameraPreviewImage.GetComponent<AspectRatioFitter>();
                if (fitter != null) Destroy(fitter);

                // Set anchor to center and specify absolute size with sizeDelta
                RectTransform rt = cameraPreviewImage.rectTransform;
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = Vector2.zero;

                int angle = webCam.videoRotationAngle;
                bool isMirrored = webCam.videoVerticallyMirrored;

                // Rotation
                rt.localEulerAngles = new Vector3(0, 0, -angle);

                // Mirroring
                Vector3 scale = Vector3.one;
                if (isMirrored)
                {
                    scale.y = -1f;
                }
                rt.localScale = scale;

                // Adjust size while maintaining aspect ratio
                RectTransform parentRect = cameraPreviewImage.transform.parent as RectTransform;
                if (parentRect == null) return;

                float parentWidth = parentRect.rect.width;
                float parentHeight = parentRect.rect.height;


                float videoWidth = webCam.width;
                float videoHeight = webCam.height;

                // Size after rotation
                float visualVideoWidth, visualVideoHeight;
                if (Mathf.Abs(angle) == 90 || Mathf.Abs(angle) == 270)
                {
                    visualVideoWidth = videoHeight;
                    visualVideoHeight = videoWidth;
                }
                else
                {
                    visualVideoWidth = videoWidth;
                    visualVideoHeight = videoHeight;
                }

                // Fit within screen (with letterboxing)
                float widthRatio = parentWidth / visualVideoWidth;
                float heightRatio = parentHeight / visualVideoHeight;
                
                float ratio = Mathf.Min(widthRatio, heightRatio);

                cameraPreviewImage.rectTransform.sizeDelta = new Vector2(videoWidth * ratio, videoHeight * ratio);
            }
            else
            {
                // ARFoundation: Adjust rotation and aspect ratio on UI side
                var fitter = cameraPreviewImage.GetComponent<AspectRatioFitter>();
                if (fitter != null) Destroy(fitter);

                RectTransform rt = cameraPreviewImage.rectTransform;
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = Vector2.zero;

                // Adjust rotation and mirroring based on screen orientation
                int angle = 0;
                Vector3 scale = new Vector3(-1f, 1f, 1f); // X-axis flip for mirroring correction

                if (Screen.orientation == ScreenOrientation.Portrait || Screen.orientation == ScreenOrientation.PortraitUpsideDown)
                {
                    angle = -90; // Rotate 90 degrees for portrait
                }

                rt.localEulerAngles = new Vector3(0, 0, -angle);
                rt.localScale = scale;

                RectTransform parentRect = cameraPreviewImage.transform.parent as RectTransform;
                if (parentRect == null) return;

                float parentWidth = parentRect.rect.width;
                float parentHeight = parentRect.rect.height;

                float texWidth = previewTexture.width;
                float texHeight = previewTexture.height;

                // Size after rotation
                float visualWidth, visualHeight;
                if (Mathf.Abs(angle) == 90 || Mathf.Abs(angle) == 270)
                {
                    visualWidth = texHeight;
                    visualHeight = texWidth;
                }
                else
                {
                    visualWidth = texWidth;
                    visualHeight = texHeight;
                }

                float widthRatio = parentWidth / visualWidth;
                float heightRatio = parentHeight / visualHeight;
                float ratio = Mathf.Min(widthRatio, heightRatio);

                cameraPreviewImage.rectTransform.sizeDelta = new Vector2(texWidth * ratio, texHeight * ratio);
            }
        }


        private void OnDestroy()
        {
            // Cleanup event listeners
            if (sensorManager != null)
            {
                sensorManager.OnSensorDataUpdated -= OnSensorDataUpdated;
            }

            // Stop and dispose FrameEncoder
            if (frameEncoder != null)
            {
                frameEncoder.Stop();
                frameEncoder.Dispose();
                frameEncoder = null;
            }
        }
        
        private void OnSensorDataUpdated(SensorData data)
        {
            // Display sensor values
            if (accelText != null)
            {
                accelText.text = $"Accel: {data.Acceleration.x:F2}, {data.Acceleration.y:F2}, {data.Acceleration.z:F2}";
            }
            if (gyroText != null)
            {
                gyroText.text = $"Gyro:  {data.Gyroscope.x:F2}, {data.Gyroscope.y:F2}, {data.Gyroscope.z:F2}";
            }
            if (magText != null)
            {
                magText.text = $"Mag:   {data.Magnetometer.x:F1}, {data.Magnetometer.y:F1}, {data.Magnetometer.z:F1}";
            }
            
            // Forward sensor data to GrpcDataStreamer
            if (grpcDataStreamer != null)
            {
                grpcDataStreamer.UpdateSensorData(data);
            }
        }
        
        private void OnFrameCaptured(CameraFrameData frameData)
        {
            // Callback on frame capture (stats updated in UpdatePipelineStats)
        }
        
        private void OnStartStopButtonClicked()
        {
            if (!isRunning)
            {
                StartCapture();
            }
            else
            {
                StopCapture();
            }
        }
        
        private void StartCapture()
        {
            isRunning = true;
            StartConnectionMonitoring();

            if (grpcDataStreamer != null)
            {
                grpcDataStreamer.StartStreaming("UI StartCapture");
            }

            // Camera capture already started in Start()
            
            UpdateStartStopButton(true);
            if (recordingIndicator != null)
            {
                recordingIndicator.enabled = true;
                StartCoroutine(BlinkRecordingIndicator());
            }
        }
        
        private void StopCapture()
        {
            // Stop only gRPC sending, continue camera capture and preview
            if (grpcDataStreamer != null)
            {
                grpcDataStreamer.StopStreaming("UI StopCapture", disableAutoResume: true);
            }

            StopConnectionMonitoring();
            
            isRunning = false;
            UpdateStartStopButton(false);
            if (recordingIndicator != null)
            {
                recordingIndicator.enabled = false;
            }
            StopAllCoroutines();
        }

        private void StartConnectionMonitoring()
        {
            if (connectionMonitorCoroutine != null)
                return;

            connectionMonitorCoroutine = StartCoroutine(ConnectionMonitorLoop());
        }

        private void StopConnectionMonitoring()
        {
            if (connectionMonitorCoroutine != null)
            {
                StopCoroutine(connectionMonitorCoroutine);
                connectionMonitorCoroutine = null;
            }

            lastConnectionState = ConnectionState.Disconnected;
            UpdateConnectionStatus(ConnectionState.Disconnected);
        }

        private System.Collections.IEnumerator ConnectionMonitorLoop()
        {
            while (isRunning)
            {
                string host = config != null ? config.Server.IpAddress : "127.0.0.1";
                int port = config != null ? config.Server.Port : 50051;

                bool reachable = false;
                yield return TryTcpReachable(host, port, timeoutSeconds: 1.5f, result => reachable = result);

                ConnectionState newState = reachable ? ConnectionState.Connected : ConnectionState.Disconnected;
                
                if (newState != lastConnectionState)
                {
                    Debug.Log($"[ConnectionMonitor] Connection state changed: {lastConnectionState} -> {newState}");
                    lastConnectionState = newState;
                    UpdateConnectionStatus(newState);
                }

                // Poll interval: 5秒（接続済み）、1秒（未接続）
                yield return new WaitForSeconds(reachable ? 5f : 1f);
            }

            connectionMonitorCoroutine = null;
        }

        private static System.Collections.IEnumerator TryTcpReachable(string host, int port, float timeoutSeconds, Action<bool> onResult)
        {
            if (string.IsNullOrWhiteSpace(host) || port <= 0 || port > 65535)
            {
                onResult?.Invoke(false);
                yield break;
            }

            Task<bool> task = Task.Run(async () =>
            {
                try
                {
                    using (var client = new TcpClient())
                    {
                        Task connectTask = client.ConnectAsync(host, port);
                        Task timeoutTask = Task.Delay(TimeSpan.FromSeconds(timeoutSeconds));
                        Task completed = await Task.WhenAny(connectTask, timeoutTask).ConfigureAwait(false);
                        if (completed != connectTask)
                            return false;

                        await connectTask.ConfigureAwait(false);
                        return client.Connected;
                    }
                }
                catch
                {
                    return false;
                }
            });

            float elapsed = 0f;
            while (!task.IsCompleted)
            {
                yield return null;
                elapsed += Time.deltaTime;
                if (elapsed > timeoutSeconds + 1f)
                {
                    onResult?.Invoke(false);
                    yield break;
                }
            }

            onResult?.Invoke(task.Status == TaskStatus.RanToCompletion && task.Result);
        }
        
        private System.Collections.IEnumerator BlinkRecordingIndicator()
        {
            while (isRunning)
            {
                if (recordingIndicator != null)
                {
                    recordingIndicator.enabled = !recordingIndicator.enabled;
                }
                yield return new WaitForSeconds(0.5f);
            }
        }
        
        private void UpdateStartStopButton(bool running)
        {
            if (startStopButtonText != null)
            {
                if (running)
                {
                    startStopButtonText.text = "Stop";
                    if (startStopButton != null)
                    {
                        startStopButton.GetComponent<Image>().color = new Color(0.8f, 0.2f, 0.2f);
                    }
                }
                else
                {
                    startStopButtonText.text = "Start";
                    if (startStopButton != null)
                    {
                        startStopButton.GetComponent<Image>().color = new Color(0.2f, 0.8f, 0.2f);
                    }
                }
            }
            else
            {
                Debug.LogError("ERROR: startStopButtonText is NULL!");
            }
        }
        
        private void OnSettingsButtonClicked()
        {
            if (settingsPanel != null)
            {
                settingsPanel.SetActive(!settingsPanel.activeSelf);
            }
        }
        
        public void UpdateConnectionStatus(ConnectionState state)
        {
            if (connectionStatusText == null)
                return;
                
            switch (state)
            {
                case ConnectionState.Connected:
                    connectionStatusText.text = "Connected";
                    connectionStatusText.color = Color.green;
                    break;
                case ConnectionState.Connecting:
                    connectionStatusText.text = "Connecting...";
                    connectionStatusText.color = Color.yellow;
                    break;
                case ConnectionState.Disconnected:
                    connectionStatusText.text = "Disconnected";
                    connectionStatusText.color = Color.red;
                    break;
            }
        }
    }
    
    /// <summary>
    /// Connection state enumeration
    /// </summary>
    public enum ConnectionState
    {
        Disconnected,
        Connecting,
        Connected
    }
}
