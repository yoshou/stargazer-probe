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
            ARFoundation    // ARFoundationCameraCapture
        }
        
        // Hard-coded setting to switch implementation
        // Change this and rebuild to switch implementation
        private const CaptureType ACTIVE_CAPTURE_TYPE = CaptureType.ARFoundation;
        
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
                    // Stop/disable AR features when using WebCamTexture
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
            // Disable AR features to avoid conflicts with Mobile(WebCamTexture) mode
            var arSession = Object.FindAnyObjectByType<UnityEngine.XR.ARFoundation.ARSession>();
            if (arSession != null)
            {
                arSession.enabled = false;
                Debug.Log("[CameraCaptureFactory] Disabled ARSession (Mobile mode)");
            }

            // Disable ARCameraBackground (prevent camera feed from appearing in background)
            var backgrounds = Object.FindObjectsByType<UnityEngine.XR.ARFoundation.ARCameraBackground>(FindObjectsSortMode.None);
            foreach (var bg in backgrounds)
            {
                bg.enabled = false;
            }
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
            
            var arCapture = gameObject.GetComponent<ARFoundationCameraCapture>();
            if (arCapture != null)
            {
                Object.Destroy(arCapture);
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
