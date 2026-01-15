using UnityEngine;

namespace StargazerProbe.Camera
{
    /// <summary>
    /// Factory class to switch camera capture implementation
    /// </summary>
    public static class CameraCaptureFactory
    {
        /// <summary>
        /// Type of camera capture to use
        /// </summary>
        public enum CaptureType
        {
            Mobile,         // MobileCameraCapture (WebCamTexture)
            Camera2         // Camera2CameraCapture (Android Camera2)
        }
        
        // Hard-coded setting to switch implementation
        // Change this and rebuild to switch implementation
    #if UNITY_ANDROID && !UNITY_EDITOR
        private const CaptureType ACTIVE_CAPTURE_TYPE = CaptureType.Camera2;
    #else
        private const CaptureType ACTIVE_CAPTURE_TYPE = CaptureType.Mobile;
    #endif
        
        /// <summary>
        /// Add appropriate camera capture component to specified GameObject
        /// </summary>
        /// <param name="gameObject">GameObject to add component to</param>
        /// <returns>Added ICameraCapture implementation</returns>
        public static ICameraCapture CreateCameraCapture(GameObject gameObject)
        {
            if (gameObject == null)
            {
                Debug.LogError("[CameraCaptureFactory] Cannot create camera capture: GameObject is null");
                return null;
            }
            
            // Show which implementation is used at startup
            Debug.Log($"[CameraCaptureFactory] CreateCameraCapture type={ACTIVE_CAPTURE_TYPE}");
            
            // Remove existing camera capture components
            RemoveExistingCaptures(gameObject);
            
            ICameraCapture capture = null;
            
#pragma warning disable CS0162 // Disable unreachable code detection
            switch (ACTIVE_CAPTURE_TYPE)
            {
                case CaptureType.Mobile:
                    Debug.Log("[CameraCaptureFactory] Adding MobileCameraCapture");
                    capture = gameObject.AddComponent<MobileCameraCapture>();
                    break;

                case CaptureType.Camera2:
                    Debug.Log("[CameraCaptureFactory] Adding Camera2CameraCapture");
                    capture = gameObject.AddComponent<Camera2CameraCapture>();
                    break;
                    
                default:
                    Debug.LogError($"[CameraCaptureFactory] Unknown capture type: {ACTIVE_CAPTURE_TYPE}");
                    break;
            }
#pragma warning restore CS0162
            
            return capture;
        }
        
        /// <summary>
        /// Remove all existing camera capture components
        /// </summary>
        private static void RemoveExistingCaptures(GameObject gameObject)
        {
            var mobileCapture = gameObject.GetComponent<MobileCameraCapture>();
            if (mobileCapture != null)
            {
                Object.Destroy(mobileCapture);
            }
            
            var camera2Capture = gameObject.GetComponent<Camera2CameraCapture>();
            if (camera2Capture != null)
            {
                Object.Destroy(camera2Capture);
            }
        }
        
        /// <summary>
        /// Get currently active capture type
        /// </summary>
        public static CaptureType GetActiveCaptureType()
        {
            return ACTIVE_CAPTURE_TYPE;
        }
    }
}
