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
        private ICameraCapture cameraCapture;  // ファクトリーで作成するため、Serializeしない
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

            // カメラキャプチャをファクトリーで作成
            cameraCapture = CameraCaptureFactory.CreateCameraCapture(gameObject);
            if (cameraCapture == null)
            {
                Debug.LogError("Failed to create camera capture");
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

            // アプリ起動直後からカメラキャプチャを開始 (プレビュー表示のため)
            if (cameraCapture != null)
            {
                cameraCapture.StartCapture();
            }
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
                var fitter = cameraPreviewImage.GetComponent<AspectRatioFitter>();
                if (fitter != null) Destroy(fitter);

                // アンカーを中央に設定してsizeDeltaで絶対サイズ指定
                RectTransform rt = cameraPreviewImage.rectTransform;
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = Vector2.zero;

                int angle = webCam.videoRotationAngle;
                bool isMirrored = webCam.videoVerticallyMirrored;

                // 回転
                rt.localEulerAngles = new Vector3(0, 0, -angle);

                // ミラーリング
                Vector3 scale = Vector3.one;
                if (isMirrored)
                {
                    scale.y = -1f;
                }
                rt.localScale = scale;

                // アスペクト比を維持してサイズ調整
                RectTransform parentRect = cameraPreviewImage.transform.parent as RectTransform;
                if (parentRect == null) return;

                float parentWidth = parentRect.rect.width;
                float parentHeight = parentRect.rect.height;


                float videoWidth = webCam.width;
                float videoHeight = webCam.height;

                // 回転後の表示サイズ
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

                // 画面内に収める（黒帯あり）
                float widthRatio = parentWidth / visualVideoWidth;
                float heightRatio = parentHeight / visualVideoHeight;
                
                float ratio = Mathf.Min(widthRatio, heightRatio);

                cameraPreviewImage.rectTransform.sizeDelta = new Vector2(videoWidth * ratio, videoHeight * ratio);
            }
            else
            {
                // ARFoundation: UI側で回転とアスペクト比を調整
                var fitter = cameraPreviewImage.GetComponent<AspectRatioFitter>();
                if (fitter != null) Destroy(fitter);

                RectTransform rt = cameraPreviewImage.rectTransform;
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = Vector2.zero;

                // 画面向きに応じて回転とミラーリングを調整
                int angle = 0;
                Vector3 scale = new Vector3(-1f, 1f, 1f); // X軸反転でミラーリング補正

                if (Screen.orientation == ScreenOrientation.Portrait || Screen.orientation == ScreenOrientation.PortraitUpsideDown)
                {
                    angle = -90; // 縦向きは90度回転
                }

                rt.localEulerAngles = new Vector3(0, 0, -angle);
                rt.localScale = scale;

                RectTransform parentRect = cameraPreviewImage.transform.parent as RectTransform;
                if (parentRect == null) return;

                float parentWidth = parentRect.rect.width;
                float parentHeight = parentRect.rect.height;

                float texWidth = previewTexture.width;
                float texHeight = previewTexture.height;

                // 回転後の表示サイズ
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
            // フレームキャプチャ時のコールバック（統計情報の更新はUpdatePipelineStatsで実施）
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

            // カメラキャプチャはStart()で既に開始済み
            
            UpdateStartStopButton(true);
            if (recordingIndicator != null)
            {
                recordingIndicator.enabled = true;
                StartCoroutine(BlinkRecordingIndicator());
            }
        }
        
        private void StopCapture()
        {
            // gRPC送信のみ停止、カメラキャプチャとプレビューは継続
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
    /// 接続状態の列挙型
    /// </summary>
    public enum ConnectionState
    {
        Disconnected,
        Connecting,
        Connected
    }
}
