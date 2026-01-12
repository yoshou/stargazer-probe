using UnityEngine;

namespace StargazerProbe.Camera
{
    /// <summary>
    /// カメラキャプチャの実装を切り替えるファクトリークラス
    /// </summary>
    public static class CameraCaptureFactory
    {
        /// <summary>
        /// 使用するカメラキャプチャのタイプ
        /// </summary>
        public enum CaptureType
        {
            Mobile,         // MobileCameraCapture (WebCamTexture)
            ARFoundation    // ARFoundationCameraCapture
        }
        
        // ハードコーディングで切り替える設定
        // ここを変更してビルドすることで実装を切り替え
        private const CaptureType ACTIVE_CAPTURE_TYPE = CaptureType.ARFoundation;
        
        /// <summary>
        /// 指定されたGameObjectに適切なカメラキャプチャコンポーネントを追加
        /// </summary>
        /// <param name="gameObject">コンポーネントを追加するGameObject</param>
        /// <returns>追加されたICameraCaptureの実装</returns>
        public static ICameraCapture CreateCameraCapture(GameObject gameObject)
        {
            if (gameObject == null)
            {
                Debug.LogError("[CameraCaptureFactory] Cannot create camera capture: GameObject is null");
                return null;
            }
            
            // 起動時にどの実装が使用されるか明示
            Debug.Log($"[CameraCaptureFactory] CreateCameraCapture type={ACTIVE_CAPTURE_TYPE}");
            
            // 既存のカメラキャプチャコンポーネントを削除
            RemoveExistingCaptures(gameObject);
            
            ICameraCapture capture = null;
            
#pragma warning disable CS0162 // 到達不可能コード検出を無効化
            switch (ACTIVE_CAPTURE_TYPE)
            {
                case CaptureType.Mobile:
                    Debug.Log("[CameraCaptureFactory] Adding MobileCameraCapture");
                    // WebCamTexture使用時はAR機能を停止・無効化する
                    DisableARComponents();
                    capture = gameObject.AddComponent<MobileCameraCapture>();
                    break;
                    
                case CaptureType.ARFoundation:
                    Debug.Log("[CameraCaptureFactory] Adding ARFoundationCameraCapture");
                    capture = gameObject.AddComponent<ARFoundationCameraCapture>();
                    break;
                    
                default:
                    Debug.LogError($"[CameraCaptureFactory] Unknown capture type: {ACTIVE_CAPTURE_TYPE}");
                    break;
            }
#pragma warning restore CS0162
            
            return capture;
        }

        private static void DisableARComponents()
        {
            // Mobile(WebCamTexture)モード時はAR機能と競合するため無効化
            var arSession = Object.FindAnyObjectByType<UnityEngine.XR.ARFoundation.ARSession>();
            if (arSession != null)
            {
                arSession.enabled = false;
                Debug.Log("[CameraCaptureFactory] Disabled ARSession (Mobile mode)");
            }

            // ARCameraBackgroundを無効化（背景にカメラ映像が表示されるのを防ぐ）
            var backgrounds = Object.FindObjectsByType<UnityEngine.XR.ARFoundation.ARCameraBackground>(FindObjectsSortMode.None);
            foreach (var bg in backgrounds)
            {
                bg.enabled = false;
            }
        }
        
        /// <summary>
        /// 既存のカメラキャプチャコンポーネントをすべて削除
        /// </summary>
        private static void RemoveExistingCaptures(GameObject gameObject)
        {
            var mobileCapture = gameObject.GetComponent<MobileCameraCapture>();
            if (mobileCapture != null)
            {
                Object.Destroy(mobileCapture);
            }
            
            var arCapture = gameObject.GetComponent<ARFoundationCameraCapture>();
            if (arCapture != null)
            {
                Object.Destroy(arCapture);
            }
        }
        
        /// <summary>
        /// 現在アクティブなキャプチャタイプを取得
        /// </summary>
        public static CaptureType GetActiveCaptureType()
        {
            return ACTIVE_CAPTURE_TYPE;
        }
    }
}
