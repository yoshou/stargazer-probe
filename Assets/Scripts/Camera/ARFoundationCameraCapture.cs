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
    /// ARFoundationを使用したカメラキャプチャとJPEG圧縮を管理するクラス。
    /// 
    /// - ARFoundationのカメラ画像とカメラ内部パラメータを取得
    /// - JPEGエンコードはバックグラウンドで実行
    /// - エンコードが追いつかない場合はフレームを間引く
    /// </summary>
    public class ARFoundationCameraCapture : MonoBehaviour, ICameraCapture
    {
        [Header("AR Components")]
        [SerializeField] private ARCameraManager arCameraManager;

        [Header("Preview")]
        [Tooltip("UIプレビューで表示するため、ARCameraBackgroundを無効化して背景にカメラ映像が出ないようにする")]
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
        
        // 状態
        public bool IsCapturing { get; private set; }
        public float ActualFPS { get; private set; }
        public int SkippedFrames { get; private set; }
        
        // イベント
        public event Action<RawCameraFrameData> OnFrameCaptured;
        public event Action OnCaptureStarted;
        public event Action OnCaptureStopped;
        public event Action<string> OnCaptureStartFailed;
        
        // 内部変数
        private float captureInterval;
        private float lastCaptureTime;
        private int consecutiveSkips;
        private Texture2D previewTexture;

        private int previewWidth;
        private int previewHeight;
        
        // カメラ内部パラメータ
        private CameraIntrinsics currentIntrinsics;
        private bool hasIntrinsics;

        private float lastCpuImageErrorLogTime;
        
        private void Awake()
        {
            captureInterval = 1f / targetFPS;
            
            // ARCameraManagerを自動検索
            if (arCameraManager == null)
            {
                arCameraManager = FindAnyObjectByType<ARCameraManager>();
            }
        }

        private void Start()
        {
            // 背景描画を無効化（UI RawImageでプレビュー表示するため）
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
        /// カメラを初期化して開始
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
            // ARセッションが開始されるまで待機
            yield return new WaitForSeconds(0.5f);
            
            // 最適な解像度を選択して設定
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
                            
                            // スコア計算: 解像度の一致を最優先し、FPSも考慮する
                            // 解像度が違うとペナルティ大
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

            // フレーム受信イベントを登録
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
            
            // FPS計算
            ActualFPS = 1f / Time.deltaTime;
            
            // 指定された間隔でキャプチャ
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
                
                // カメラ画像を取得
                if (!arCameraManager.TryAcquireLatestCpuImage(out XRCpuImage image))
                    return;
                
                try
                {
                    // カメラ内部パラメータを取得（CPU画像を再取得しない）
                    UpdateIntrinsics(image.width, image.height);
                    
                    // 画像データをColor32配列に変換
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
                    
                    // Color32配列に変換
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

                    // UIプレビュー用テクスチャを更新（メインスレッド）
                    UpdatePreviewTexture(image.width, image.height, pixels);
                    
                    // 生データをイベントで発行
                    OnFrameCaptured?.Invoke(new RawCameraFrameData
                    {
                        Timestamp = Time.realtimeSinceStartup,
                        Width = image.width,
                        Height = image.height,
                        Pixels = pixels,
                        Intrinsics = hasIntrinsics ? currentIntrinsics : default,
                        ReturnBufferCallback = null  // ARFoundationはバッファプーリングなし
                    });
                }
                catch (InvalidOperationException ex)
                {
                    // 一部端末/起動直後に一時的に発生しうるため、ログを間引く
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

            // ARFoundation標準APIで内部パラメータを取得（CPU画像の再取得はしない）
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
        /// カメラを停止
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
        /// カメラ設定を変更
        /// </summary>
        public void UpdateSettings(int newWidth, int newHeight, int newFPS, int newQuality)
        {
            // ARFoundationでは解像度は変更できないため、FPSと品質のみ更新
            targetFPS = newFPS;
            jpegQuality = Mathf.Clamp(newQuality, 1, 100);
            captureInterval = 1f / targetFPS;

            Debug.Log($"AR Camera settings updated: {targetFPS}fps, quality={jpegQuality}");
        }

        /// <summary>
        /// プレビュー用のテクスチャを取得
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
