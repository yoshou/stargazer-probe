using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using StargazerProbe.Camera;
using StargazerProbe.Config;
using StargazerProbe.Sensors;

namespace StargazerProbe.Grpc
{
    /// <summary>
    /// カメラフレーム + 最新IMUをまとめて gRPC (Duplex Streaming) で送信する。
    /// 
    /// 送信は「遅延許容・ドロップ最小化」方針：
    /// - OnFrameCaptured では送信せず、送信キューに積むだけ
    /// - バックグラウンド送信ループが順番に SendAsync する
    /// - キューが上限を超えた場合のみ、古いパケットから捨てる（メモリ上限を守るため）
    /// </summary>
    public class GrpcDataStreamer : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private IMUSensorManager sensorManager;
        [SerializeField] private MobileCameraCapture cameraCapture;

        [Header("Options")]
        [SerializeField] private string deviceIdOverride;

        private SystemConfig config;
        private GrpcStreamClient grpcClient;
        private CancellationTokenSource cts;
        private bool isStopping;

        // 送信キュー：順序を保って送る。接続状況により滞留することがある。
        private readonly ConcurrentQueue<Stargazer.DataPacket> sendQueue = new ConcurrentQueue<Stargazer.DataPacket>();
        private readonly SemaphoreSlim sendSignal = new SemaphoreSlim(0);
        private CancellationTokenSource sendLoopCts;
        private Task sendLoopTask;
        private int queuedPackets;

        private SensorData lastSensor;
        private bool hasSensor;

        private bool wasStreamingBeforePause;
        private bool resumePending;
        private bool isPaused;

        private int framesCaptured;
        private int framesSent;
        private int framesSkippedNotConnected;
        private int framesSkippedStopping;
        private int sendErrors;
        private float lastStatsLogTime;

        private int responsesReceived;
        private int responsesFailed;
        private float lastResponseSummaryLogTime;

        // Drop 指標：実際に捨てるのは FramesDroppedQueueOverflow（バッファ上限超過時）のみ。
        private int framesDroppedQueueOverflow;

        public int PendingSendOps => Volatile.Read(ref queuedPackets);
        public int FramesCaptured => framesCaptured;
        public int FramesSent => framesSent;
        public int FramesSkippedNotConnected => framesSkippedNotConnected;
        public int FramesSkippedStopping => framesSkippedStopping;
        public int SendErrors => sendErrors;
        public int FramesDroppedQueueOverflow => framesDroppedQueueOverflow;
        public int ResponsesReceived => responsesReceived;
        public int ResponsesFailed => responsesFailed;

        public event Action<GrpcConnectionState> OnGrpcStateChanged;

        private void Awake()
        {
            config = SystemConfig.Instance;

            RefreshReferences();

            grpcClient = new GrpcStreamClient(SynchronizationContext.Current);
            grpcClient.OnStateChanged += state => OnGrpcStateChanged?.Invoke(state);
            grpcClient.OnResponse += OnGrpcResponse;
        }

        private void StartSendLoop()
        {
            if (sendLoopTask != null && !sendLoopTask.IsCompleted)
                return;

            sendLoopCts?.Cancel();
            sendLoopCts?.Dispose();
            sendLoopCts = new CancellationTokenSource();

            // Reset queue accounting
            while (sendQueue.TryDequeue(out _)) { }
            Interlocked.Exchange(ref queuedPackets, 0);

            sendLoopTask = Task.Run(() => SendLoopAsync(sendLoopCts.Token));
        }

        private void StopSendLoop()
        {
            try
            {
                sendLoopCts?.Cancel();
                try { sendSignal.Release(); } catch { }
            }
            catch { }

            // Drain queue
            while (sendQueue.TryDequeue(out _)) { }
            Interlocked.Exchange(ref queuedPackets, 0);

            sendLoopCts?.Dispose();
            sendLoopCts = null;
            sendLoopTask = null;
        }

        private async Task SendLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await sendSignal.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                if (cancellationToken.IsCancellationRequested)
                    break;

                // If not connected yet, wait a little and retry.
                if (grpcClient == null || !grpcClient.IsConnected)
                {
                    try { await Task.Delay(10, cancellationToken).ConfigureAwait(false); } catch { }
                    continue;
                }

                if (!sendQueue.TryDequeue(out var packet) || packet == null)
                    continue;

                Interlocked.Decrement(ref queuedPackets);

                try
                {
                    await grpcClient.SendAsync(packet).ConfigureAwait(false);
                    Interlocked.Increment(ref framesSent);
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref sendErrors);
                    Debug.LogError($"[GrpcDataStreamer] Send failed: {ex.Message}");
                }
            }
        }

        private void OnGrpcResponse(Stargazer.DataResponse resp)
        {
            if (resp == null)
                return;

            responsesReceived++;

            if (!resp.Success)
            {
                responsesFailed++;
                Debug.LogError($"[GrpcDataStreamer] Response failed: received={resp.ReceivedPackets} msg={resp.Message}");
                return;
            }

            // Too noisy to log every response. In debug builds, log an occasional summary.
            if (Debug.isDebugBuild)
            {
                const float intervalSeconds = 2f;
                float now = Time.realtimeSinceStartup;
                if (now - lastResponseSummaryLogTime >= intervalSeconds)
                {
                    lastResponseSummaryLogTime = now;
                    Debug.Log($"[GrpcDataStreamer] Response ok: last_received={resp.ReceivedPackets} total_responses={responsesReceived} failed={responsesFailed}");
                }
            }
        }

        private void RefreshReferences()
        {
            if (sensorManager == null)
                sensorManager = FindAnyObjectByType<IMUSensorManager>();
            if (cameraCapture == null)
                cameraCapture = FindAnyObjectByType<MobileCameraCapture>();
        }

        public async void StartStreaming(string reason = null)
        {
            if (isPaused)
                return;

            if (cts != null || isStopping)
                return;

            resumePending = false;

            framesCaptured = 0;
            framesSent = 0;
            framesSkippedNotConnected = 0;
            framesSkippedStopping = 0;
            sendErrors = 0;
            lastStatsLogTime = 0f;

            responsesReceived = 0;
            responsesFailed = 0;
            lastResponseSummaryLogTime = 0f;

            RefreshReferences();

            cts = new CancellationTokenSource();

            StartSendLoop();

            if (sensorManager != null)
                sensorManager.OnSensorDataUpdated += OnSensorDataUpdated;

            if (cameraCapture != null)
                cameraCapture.OnFrameCaptured += OnFrameCaptured;

            string host = config != null ? config.Server.IpAddress : "127.0.0.1";
            int port = config != null ? config.Server.Port : 50051;

            Debug.Log($"[GrpcDataStreamer] StartStreaming -> {host}:{port} reason={(string.IsNullOrWhiteSpace(reason) ? "(none)" : reason)} camera={(cameraCapture == null ? "null" : $"{cameraCapture.name}#{cameraCapture.GetInstanceID()} capturing={cameraCapture.IsCapturing}")}");

            try
            {
                await grpcClient.ConnectAsync(host, port, cts.Token);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GrpcDataStreamer] Connect failed: {ex.Message}");
            }
        }

        public async void StopStreaming(string reason = null, bool disableAutoResume = false)
        {
            if (cts == null)
                return;

            Debug.Log($"[GrpcDataStreamer] StopStreaming reason={(string.IsNullOrWhiteSpace(reason) ? "(none)" : reason)}");

            isStopping = true;

            // Close the send gate immediately to prevent races with OnFrameCaptured.
            var localCts = cts;
            cts = null;

            if (disableAutoResume)
            {
                wasStreamingBeforePause = false;
                resumePending = false;
            }

            try
            {
                if (sensorManager != null)
                    sensorManager.OnSensorDataUpdated -= OnSensorDataUpdated;

                if (cameraCapture != null)
                    cameraCapture.OnFrameCaptured -= OnFrameCaptured;

                localCts.Cancel();
                StopSendLoop();
                await grpcClient.DisconnectAsync();
            }
            finally
            {
                localCts.Dispose();
                hasSensor = false;
                isStopping = false;

                if (resumePending && !isPaused)
                {
                    resumePending = false;
                    StartStreaming("app resume (deferred)");
                }
            }
        }

        private void OnSensorDataUpdated(SensorData data)
        {
            lastSensor = data;
            hasSensor = true;
        }

        private async void OnFrameCaptured(CameraFrameData frameData)
        {
            framesCaptured++;

            if (cts == null || isStopping || grpcClient == null || !grpcClient.IsConnected)
            {
                if (isStopping)
                    framesSkippedStopping++;
                else
                    framesSkippedNotConnected++;

                LogFrameStatsThrottled("skipping");
                return;
            }

            var packet = new Stargazer.DataPacket
            {
                Timestamp = frameData.Timestamp,
                DeviceId = string.IsNullOrWhiteSpace(deviceIdOverride) ? SystemInfo.deviceUniqueIdentifier : deviceIdOverride,
                Camera = ProtoConverters.ToProto(frameData)
            };

            if (hasSensor)
                packet.Sensor = ProtoConverters.ToProto(lastSensor);

            int maxBuffer = config != null && config.Advanced != null ? Mathf.Max(1, config.Advanced.MaxBufferSize) : 100;

            sendQueue.Enqueue(packet);
            int q = Interlocked.Increment(ref queuedPackets);
            sendSignal.Release();

            // If queue grows beyond cap, drop oldest packets (delay is allowed, but memory isn't).
            while (q > maxBuffer && sendQueue.TryDequeue(out _))
            {
                q = Interlocked.Decrement(ref queuedPackets);
                framesDroppedQueueOverflow++;
            }

            // Keep stats logs consistent
            LogFrameStatsThrottled("enqueued");
        }

        private void LogFrameStatsThrottled(string tag)
        {
            if (!Debug.isDebugBuild)
                return;

            const float intervalSeconds = 2f;
            float now = Time.realtimeSinceStartup;
            if (now - lastStatsLogTime < intervalSeconds)
                return;

            lastStatsLogTime = now;

            string conn = grpcClient == null ? "grpcClient=null" : $"state={grpcClient.State} isConnected={grpcClient.IsConnected}";
            Debug.Log($"[GrpcDataStreamer] FrameStats({tag}): captured={framesCaptured} sent={framesSent} skipNotConn={framesSkippedNotConnected} skipStopping={framesSkippedStopping} sendErrors={sendErrors} {conn}");
        }

        private void OnDestroy()
        {
            Debug.Log("[GrpcDataStreamer] OnDestroy");
            if (sensorManager != null)
                sensorManager.OnSensorDataUpdated -= OnSensorDataUpdated;

            if (cameraCapture != null)
                cameraCapture.OnFrameCaptured -= OnFrameCaptured;

            if (cts != null)
            {
                cts.Cancel();
                cts.Dispose();
                cts = null;
            }
        }

        private void OnApplicationPause(bool pause)
        {
            Debug.Log($"[GrpcDataStreamer] OnApplicationPause pause={pause}");

            isPaused = pause;

            if (pause)
            {
                wasStreamingBeforePause = cts != null;
                if (wasStreamingBeforePause)
                {
                    resumePending = true;
                    StopStreaming("app pause");
                }

                return;
            }

            if (wasStreamingBeforePause)
            {
                wasStreamingBeforePause = false;

                // If StopStreaming is still in progress, defer restart until it completes.
                if (cts != null)
                {
                    resumePending = true;
                    Debug.Log("[GrpcDataStreamer] Resume requested while stopping; deferring restart");
                    return;
                }

                resumePending = false;
                StartStreaming("app resume");
            }
        }
    }
}
