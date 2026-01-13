using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace StargazerProbe.Camera
{
    /// <summary>
    /// モバイルカメラのキャプチャとJPEG圧縮を管理するクラス。
    /// 
    /// - ピクセル取得はメインスレッド（WebCamTextureの制約）
    /// - JPEGエンコードはバックグラウンドで実行して、メインスレッドのフレーム落ちを抑える
    /// - エンコードが追いつかない場合はフレームを間引く（遅延ではなくFPS維持を優先）
    /// </summary>
    public class MobileCameraCapture : MonoBehaviour, ICameraCapture
    {
        [Header("Camera Settings")]
        [SerializeField] private int width = 1280;
        [SerializeField] private int height = 720;
        [SerializeField] private int targetFPS = 30;
        
        [Header("JPEG Settings")]
        [SerializeField] private int jpegQuality = 75;
        [SerializeField] private bool autoAdjustQuality = true;
        
        [Header("Performance")]
        [SerializeField] private int maxSkipFrames = 3;

        [Tooltip("JPEGエンコードのバックログ上限。超えたらフレームを間引く")]
        [SerializeField] private int maxPendingEncodes = 2;

        [Tooltip("エンコード用ピクセルバッファ数。多いほどGCを抑えつつ追従しやすいがメモリを使う")]
        [SerializeField] private int encoderBufferCount = 3;
        
        // カメラ
        private WebCamTexture webCamTexture;

        private int bufferWidth;
        private int bufferHeight;

        private readonly object bufferLock = new object();
        private Queue<Color32[]> availableBuffers;

        private CameraFrameEncoder frameEncoder;
        
        // 状態
        public bool IsCapturing { get; private set; }
        public float ActualFPS { get; private set; }
        public int SkippedFrames { get; private set; }

        public int PendingEncodes => frameEncoder != null ? frameEncoder.PendingCount : 0;
        public int MaxPendingEncodes => maxPendingEncodes;
        
        // イベント
        public event Action<CameraFrameData> OnFrameCaptured;
        public event Action OnCaptureStarted;
        public event Action OnCaptureStopped;
        public event Action<string> OnCaptureStartFailed;
        
        // 内部変数
        private float captureInterval;
        private float lastCaptureTime;
        private int consecutiveSkips;
        
        private void Awake()
        {
            captureInterval = 1f / targetFPS;
        }
        
        /// <summary>
        /// カメラを初期化して開始
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
            // カメラデバイスを取得
            WebCamDevice[] devices = WebCamTexture.devices;
            if (devices.Length == 0)
            {
                Debug.LogError("No camera devices found");
                OnCaptureStartFailed?.Invoke("No camera devices found");
                yield break;
            }
            
            // リアカメラを優先的に選択
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
            
            // WebCamTextureを初期化
            webCamTexture = new WebCamTexture(deviceName, width, height, targetFPS);
            webCamTexture.Play();

            // カメラが起動し、解像度が確定するまで待機
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
            
            // キャプチャ用テクスチャを作成
            int w = webCamTexture.width;
            int h = webCamTexture.height;

            bufferWidth = w;
            bufferHeight = h;

            int buffers = Mathf.Max(2, encoderBufferCount);
            availableBuffers = new Queue<Color32[]>(buffers);
            for (int i = 0; i < buffers; i++)
            {
                availableBuffers.Enqueue(new Color32[w * h]);
            }

            if (frameEncoder == null)
            {
                frameEncoder = new CameraFrameEncoder(SynchronizationContext.Current);
                frameEncoder.OnFrameEncoded += frameData => OnFrameCaptured?.Invoke(frameData);
            }
            frameEncoder.Start();
            
            IsCapturing = true;
            lastCaptureTime = Time.unscaledTime;
            
            Debug.Log($"[MobileCameraCapture] Started resolution={webCamTexture.width}x{webCamTexture.height} fps={targetFPS}");
            OnCaptureStarted?.Invoke();
        }
        
        private void Update()
        {
            if (!IsCapturing || webCamTexture == null || !webCamTexture.isPlaying)
                return;

            // 画面回転などでカメラの実解像度が変わることがある。
            // バッファが古いと内部で確保が走ったり、データが欠落するので作り直す。
            // （この再構築は、エンコードループを停止→キューを排出→バッファを作り直す流れ。）
            int w = webCamTexture.width;
            int h = webCamTexture.height;
            if (w > 16 && h > 16 && (w != bufferWidth || h != bufferHeight))
            {
                RebuildEncoderBuffers(w, h);
            }
            
            // FPS計算
            ActualFPS = 1f / Time.deltaTime;
            
            // 指定された間隔でキャプチャ
            if (Time.unscaledTime - lastCaptureTime >= captureInterval)
            {
                CaptureFrame();
                lastCaptureTime = Time.unscaledTime;
            }
        }

        private void RebuildEncoderBuffers(int w, int h)
        {
            Debug.Log($"Camera resolution changed: {bufferWidth}x{bufferHeight} -> {w}x{h}. Rebuilding buffers.");

            bufferWidth = w;
            bufferHeight = h;

            int buffers = Mathf.Max(2, encoderBufferCount);
            availableBuffers = new Queue<Color32[]>(buffers);
            for (int i = 0; i < buffers; i++)
            {
                availableBuffers.Enqueue(new Color32[w * h]);
            }
        }
        
        private void CaptureFrame()
        {
            // エンコードが追いつかない場合は間引く（メインスレッドのFPS維持を優先）。
            // ここで溜め続けると遅延が増えるだけでなく、メモリ増加やGC負荷につながる。
            if (PendingEncodes >= maxPendingEncodes)
            {
                consecutiveSkips++;
                SkippedFrames++;
                
                if (consecutiveSkips >= maxSkipFrames)
                {
                    Debug.LogWarning($"Skipped {consecutiveSkips} consecutive frames");
                    
                    // 自動品質調整
                    if (autoAdjustQuality && jpegQuality > 50)
                    {
                        jpegQuality = Mathf.Max(50, jpegQuality - 10);
                        Debug.Log($"Auto-adjusted JPEG quality to {jpegQuality}");
                    }
                }
                return;
            }

            Color32[] buffer = RentPixelBuffer();
            if (buffer == null)
            {
                consecutiveSkips++;
                SkippedFrames++;
                return;
            }

            consecutiveSkips = 0;

            // WebCamTextureからのピクセル取得はメインスレッドでしか安全に呼べない。
            buffer = webCamTexture.GetPixels32(buffer);

            int w = webCamTexture.width;
            int h = webCamTexture.height;

            if (frameEncoder == null)
            {
                ReturnPixelBuffer(buffer);
                return;
            }

            if (!frameEncoder.TryEnqueue(
                    Time.realtimeSinceStartup,
                    w,
                    h,
                    jpegQuality,
                    buffer,
                    default,
                    ReturnPixelBuffer))
            {
                ReturnPixelBuffer(buffer);
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

            // 解像度変更などでサイズ不一致のバッファが返ってきた場合は再利用しない。
            int expected = bufferWidth > 0 && bufferHeight > 0 ? bufferWidth * bufferHeight : buffer.Length;
            if (buffer.Length != expected)
                return;

            lock (bufferLock)
            {
                availableBuffers.Enqueue(buffer);
            }
        }
        
        /// <summary>
        /// カメラを停止
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

            frameEncoder?.Stop();
        }
        
        /// <summary>
        /// カメラ設定を変更
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
            jpegQuality = Mathf.Clamp(newQuality, 1, 100);
            captureInterval = 1f / targetFPS;
            
            if (wasCapturing)
            {
                StartCapture();
            }
        }
        
        /// <summary>
        /// プレビュー用のテクスチャを取得
        /// </summary>
        public Texture GetPreviewTexture()
        {
            return webCamTexture;
        }
        
        private void OnDestroy()
        {
            StopCapture();

            if (frameEncoder != null)
            {
                frameEncoder.Dispose();
                frameEncoder = null;
            }
        }
    }
}
