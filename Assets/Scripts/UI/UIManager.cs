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
    /// メインUIを管理するクラス
    /// </summary>
    public class UIManager : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private IMUSensorManager sensorManager;
        [SerializeField] private MobileCameraCapture cameraCapture;
        [SerializeField] private GrpcDataStreamer grpcDataStreamer;
        
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
        
        private SystemConfig config;
        private bool isRunning = false;
        private Coroutine connectionMonitorCoroutine;
        private ConnectionState lastConnectionState = ConnectionState.Disconnected;
        private float fpsUpdateInterval = 0.5f;
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
        }
        
        private void InitializeUI()
        {
            // 初期状態
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
            // ボタンイベント
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
            
            // センサーイベント
            if (sensorManager != null)
            {
                sensorManager.OnSensorDataUpdated += OnSensorDataUpdated;
            }
            
            // カメライベント
            if (cameraCapture != null)
            {
                cameraCapture.OnFrameCaptured += OnFrameCaptured;
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

            // レイアウト崩れを避けるため、表示は短く保つ。
            // ここで表示している Drop は「送信キューが上限を超えたため古いパケットを捨てた回数」。
            // ネットワーク切断・送信エラー（SendErrors）とは別の指標。
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
                // Remove AspectRatioFitter if exists, as we will handle layout manually
                var fitter = cameraPreviewImage.GetComponent<AspectRatioFitter>();
                if (fitter != null) Destroy(fitter);

                // Reset Anchors to Center so sizeDelta works as absolute size
                RectTransform rt = cameraPreviewImage.rectTransform;
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = Vector2.zero;

                int angle = webCam.videoRotationAngle;
                bool isMirrored = webCam.videoVerticallyMirrored;

                // 1. Rotation
                rt.localEulerAngles = new Vector3(0, 0, -angle);

                // 2. Scale / Mirroring
                Vector3 scale = Vector3.one;
                if (isMirrored)
                {
                    scale.y = -1f;
                }
                rt.localScale = scale;

                // 3. Size (Aspect Ratio Fitting)
                RectTransform parentRect = cameraPreviewImage.transform.parent as RectTransform;
                if (parentRect == null) return;

                float parentWidth = parentRect.rect.width;
                float parentHeight = parentRect.rect.height;


                float videoWidth = webCam.width;
                float videoHeight = webCam.height;

                // Visual dimensions after rotation
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

                // Fit Parent (Inside) - 画角を維持するため全体を表示する（黒帯が出る可能性があるが、映像は欠けない）
                float widthRatio = parentWidth / visualVideoWidth;
                float heightRatio = parentHeight / visualVideoHeight;
                
                float ratio = Mathf.Min(widthRatio, heightRatio);

                cameraPreviewImage.rectTransform.sizeDelta = new Vector2(videoWidth * ratio, videoHeight * ratio);
            }
        }


        private void OnDestroy()
        {
            // イベントリスナーのクリーンアップ
            if (sensorManager != null)
            {
                sensorManager.OnSensorDataUpdated -= OnSensorDataUpdated;
            }

            if (cameraCapture != null)
            {
                cameraCapture.OnFrameCaptured -= OnFrameCaptured;
            }
        }
        
        private void OnSensorDataUpdated(SensorData data)
        {
            // センサー値を表示
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
        }
        
        private void OnFrameCaptured(CameraFrameData frameData)
        {
            // 表示更新は一定間隔で実施（UpdatePipelineStats）。
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

            // カメラ開始
            if (cameraCapture != null)
            {
                cameraCapture.StartCapture();
            }
            
            UpdateStartStopButton(true);
            if (recordingIndicator != null)
            {
                recordingIndicator.enabled = true;
                StartCoroutine(BlinkRecordingIndicator());
            }
        }
        
        private void StopCapture()
        {
            // カメラ停止
            if (cameraCapture != null)
            {
                cameraCapture.StopCapture();
            }

            if (grpcDataStreamer != null)
            {
                grpcDataStreamer.StopStreaming("UI StopCapture", disableAutoResume: true);
            }

            StopConnectionMonitoring();

            // Preview Reset
            if (cameraPreviewImage != null)
            {
                cameraPreviewImage.texture = null;
                cameraPreviewImage.color = new Color(0.2f, 0.2f, 0.2f);
                
                // Reset Transforms
                cameraPreviewImage.rectTransform.localEulerAngles = Vector3.zero;
                cameraPreviewImage.rectTransform.localScale = Vector3.one;
                
                // Reset Layout to Stretch/Fill
                cameraPreviewImage.rectTransform.anchorMin = Vector2.zero;
                cameraPreviewImage.rectTransform.anchorMax = Vector2.one;
                cameraPreviewImage.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                cameraPreviewImage.rectTransform.anchoredPosition = Vector2.zero;
                cameraPreviewImage.rectTransform.sizeDelta = Vector2.zero;
            }
            
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
        
        // OnDestroy moved above (also disposes preview material)
    }
    
    /// <summary>
    /// 接続状態の列挙型
    /// </summary>
    public enum ConnectionState
    {
        Disconnected,
        Connecting,
        Connected
    }
}
