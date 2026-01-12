using System;
using System.Collections;
using UnityEngine;

namespace StargazerProbe.Camera
{
    /// <summary>
    /// モバイルカメラのキャプチャとJPEG圧縮を管理するクラス
    /// </summary>
    public class MobileCameraCapture : MonoBehaviour
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
        
        // カメラ
        private WebCamTexture webCamTexture;
        private Texture2D captureTexture;
        
        // 状態
        public bool IsCapturing { get; private set; }
        public float ActualFPS { get; private set; }
        public int SkippedFrames { get; private set; }
        
        // イベント
        public event Action<CameraFrameData> OnFrameCaptured;
        public event Action OnCaptureStarted;
        public event Action OnCaptureStopped;
        public event Action<string> OnCaptureStartFailed;
        
        // 内部変数
        private float captureInterval;
        private float lastCaptureTime;
        private bool isProcessing;
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
            
            // カメラが起動するまで待機
            yield return new WaitForSeconds(1f);
            
            if (!webCamTexture.isPlaying)
            {
                Debug.LogError("Failed to start camera");
                OnCaptureStartFailed?.Invoke("Failed to start camera");
                yield break;
            }
            
            // キャプチャ用テクスチャを作成
            captureTexture = new Texture2D(webCamTexture.width, webCamTexture.height, TextureFormat.RGB24, false);
            
            IsCapturing = true;
            lastCaptureTime = Time.time;
            
            Debug.Log($"Camera started: {webCamTexture.width}x{webCamTexture.height} @ {targetFPS}fps");
            OnCaptureStarted?.Invoke();
        }
        
        private void Update()
        {
            if (!IsCapturing || webCamTexture == null || !webCamTexture.isPlaying)
                return;
            
            // FPS計算
            ActualFPS = 1f / Time.deltaTime;
            
            // 指定された間隔でキャプチャ
            if (Time.time - lastCaptureTime >= captureInterval)
            {
                CaptureFrame();
                lastCaptureTime = Time.time;
            }
        }
        
        private void CaptureFrame()
        {
            // 前のフレームがまだ処理中の場合
            if (isProcessing)
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
            
            consecutiveSkips = 0;
            StartCoroutine(ProcessFrame());
        }
        
        private IEnumerator ProcessFrame()
        {
            isProcessing = true;
            
            double timestamp = Time.realtimeSinceStartup;
            
            // WebCamTextureからピクセルデータをコピー
            captureTexture.SetPixels(webCamTexture.GetPixels());
            captureTexture.Apply();
            
            // JPEG圧縮（メインスレッドで実行）
            byte[] jpegData = captureTexture.EncodeToJPG(jpegQuality);
            
            // フレームデータを作成
            CameraFrameData frameData = new CameraFrameData
            {
                Timestamp = timestamp,
                ImageData = jpegData,
                Width = captureTexture.width,
                Height = captureTexture.height,
                Quality = jpegQuality
            };
            
            // イベント発火
            OnFrameCaptured?.Invoke(frameData);
            
            isProcessing = false;
            
            yield return null;
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
            
            if (captureTexture != null)
            {
                Destroy(captureTexture);
                captureTexture = null;
            }
            
            Debug.Log("Camera stopped");
            OnCaptureStopped?.Invoke();
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
        }
    }
    
    /// <summary>
    /// カメラフレームデータ構造体
    /// </summary>
    [Serializable]
    public struct CameraFrameData
    {
        public double Timestamp;
        public byte[] ImageData;
        public int Width;
        public int Height;
        public int Quality;
    }
}
