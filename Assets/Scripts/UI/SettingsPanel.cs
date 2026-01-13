using UnityEngine;
using UnityEngine.UI;
using TMPro;
using StargazerProbe.Config;
using StargazerProbe.Sensors;
using StargazerProbe.Camera;

namespace StargazerProbe.UI
{
    /// <summary>
    /// Manages the settings panel
    /// </summary>
    public class SettingsPanel : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private IMUSensorManager sensorManager;
        [SerializeField] private MobileCameraCapture cameraCapture;
        
        [Header("Buttons")]
        [SerializeField] private Button backButton;
        [SerializeField] private Button saveButton;
        [SerializeField] private Button resetButton;
        
        [Header("Server Settings")]
        [SerializeField] private TMP_InputField ipAddressInput;
        [SerializeField] private TMP_InputField portInput;
        
        [Header("Camera Settings")]
        [SerializeField] private TMP_Dropdown resolutionDropdown;
        [SerializeField] private TMP_Dropdown fpsDropdown;
        [SerializeField] private Slider jpegQualitySlider;
        [SerializeField] private TextMeshProUGUI jpegQualityText;
        
        [Header("Sensor Settings")]
        [SerializeField] private TMP_Dropdown samplingRateDropdown;
        [SerializeField] private Toggle enableAccelToggle;
        [SerializeField] private Toggle enableGyroToggle;
        [SerializeField] private Toggle enableMagToggle;
        
        [Header("Advanced Settings")]
        [SerializeField] private Toggle autoAdjustQualityToggle;
        [SerializeField] private Toggle enableFrameSkipToggle;
        [SerializeField] private Toggle showDebugInfoToggle;
        
        private SystemConfig config;
        
        private void Start()
        {
            config = SystemConfig.Instance;
            LoadCurrentSettings();
            SetupEventListeners();
        }
        
        private void OnEnable()
        {
            if (config != null)
            {
                LoadCurrentSettings();
            }
        }
        
        private void SetupEventListeners()
        {
            // Buttons
            if (backButton != null)
            {
                backButton.onClick.AddListener(OnBackButtonClicked);
            }
            if (saveButton != null)
            {
                saveButton.onClick.AddListener(OnSaveButtonClicked);
            }
            if (resetButton != null)
            {
                resetButton.onClick.AddListener(OnResetButtonClicked);
            }
            
            // JPEG Quality Slider
            if (jpegQualitySlider != null)
            {
                jpegQualitySlider.onValueChanged.AddListener(OnJpegQualityChanged);
            }
        }
        
        private void LoadCurrentSettings()
        {
            // Server Settings
            if (ipAddressInput != null)
            {
                ipAddressInput.text = config.Server.IpAddress;
            }
            if (portInput != null)
            {
                portInput.text = config.Server.Port.ToString();
            }
            
            // Camera Settings
            if (resolutionDropdown != null)
            {
                int resIndex = GetResolutionIndex(config.Camera.Width, config.Camera.Height);
                resolutionDropdown.value = resIndex;
            }
            if (fpsDropdown != null)
            {
                int fpsIndex = GetFPSIndex(config.Camera.TargetFPS);
                fpsDropdown.value = fpsIndex;
            }
            if (jpegQualitySlider != null)
            {
                jpegQualitySlider.value = config.Camera.JpegQuality;
                UpdateJpegQualityText(config.Camera.JpegQuality);
            }
            
            // Sensor Settings
            if (samplingRateDropdown != null)
            {
                int rateIndex = GetSamplingRateIndex(config.Sensor.SamplingRate);
                samplingRateDropdown.value = rateIndex;
            }
            if (enableAccelToggle != null)
            {
                enableAccelToggle.isOn = config.Sensor.EnableAccelerometer;
            }
            if (enableGyroToggle != null)
            {
                enableGyroToggle.isOn = config.Sensor.EnableGyroscope;
            }
            if (enableMagToggle != null)
            {
                enableMagToggle.isOn = config.Sensor.EnableMagnetometer;
            }
            
            // Advanced Settings
            if (autoAdjustQualityToggle != null)
            {
                autoAdjustQualityToggle.isOn = config.Advanced.AutoAdjustQuality;
            }
            if (enableFrameSkipToggle != null)
            {
                enableFrameSkipToggle.isOn = config.Advanced.EnableFrameSkip;
            }
            if (showDebugInfoToggle != null)
            {
                showDebugInfoToggle.isOn = config.Advanced.ShowDebugInfo;
            }
        }
        
        private void OnJpegQualityChanged(float value)
        {
            UpdateJpegQualityText((int)value);
        }
        
        private void UpdateJpegQualityText(int quality)
        {
            if (jpegQualityText != null)
            {
                jpegQualityText.text = quality.ToString();
            }
        }
        
        private void OnSaveButtonClicked()
        {
            // Read UI values
            if (ipAddressInput != null)
            {
                config.Server.IpAddress = ipAddressInput.text;
            }
            if (portInput != null && int.TryParse(portInput.text, out int port))
            {
                config.Server.Port = port;
            }
            
            // Camera settings
            if (resolutionDropdown != null)
            {
                (int w, int h) = GetResolutionFromIndex(resolutionDropdown.value);
                config.Camera.Width = w;
                config.Camera.Height = h;
            }
            if (fpsDropdown != null)
            {
                config.Camera.TargetFPS = GetFPSFromIndex(fpsDropdown.value);
            }
            if (jpegQualitySlider != null)
            {
                config.Camera.JpegQuality = (int)jpegQualitySlider.value;
            }
            
            // Sensor settings
            if (samplingRateDropdown != null)
            {
                config.Sensor.SamplingRate = GetSamplingRateFromIndex(samplingRateDropdown.value);
            }
            if (enableAccelToggle != null)
            {
                config.Sensor.EnableAccelerometer = enableAccelToggle.isOn;
            }
            if (enableGyroToggle != null)
            {
                config.Sensor.EnableGyroscope = enableGyroToggle.isOn;
            }
            if (enableMagToggle != null)
            {
                config.Sensor.EnableMagnetometer = enableMagToggle.isOn;
            }
            
            // Advanced settings
            if (autoAdjustQualityToggle != null)
            {
                config.Advanced.AutoAdjustQuality = autoAdjustQualityToggle.isOn;
            }
            if (enableFrameSkipToggle != null)
            {
                config.Advanced.EnableFrameSkip = enableFrameSkipToggle.isOn;
            }
            if (showDebugInfoToggle != null)
            {
                config.Advanced.ShowDebugInfo = showDebugInfoToggle.isOn;
            }
            
            // Save to PlayerPrefs
            config.SaveSettings();
            
            // Apply settings to running components
            ApplySettings();
            
            Debug.Log("Settings saved and applied");
            gameObject.SetActive(false);
        }
        
        private void ApplySettings()
        {
            // Apply camera settings
            if (cameraCapture != null)
            {
                cameraCapture.UpdateSettings(
                    config.Camera.Width,
                    config.Camera.Height,
                    config.Camera.TargetFPS,
                    config.Camera.JpegQuality
                );
            }
            
            // Apply sensor settings
            if (sensorManager != null)
            {
                sensorManager.SetSamplingRate(config.Sensor.SamplingRate);
                sensorManager.SetSensorEnabled(SensorType.Accelerometer, config.Sensor.EnableAccelerometer);
                sensorManager.SetSensorEnabled(SensorType.Gyroscope, config.Sensor.EnableGyroscope);
                sensorManager.SetSensorEnabled(SensorType.Magnetometer, config.Sensor.EnableMagnetometer);
            }
        }
        
        private void OnResetButtonClicked()
        {
            config.ResetToDefaults();
            LoadCurrentSettings();
            Debug.Log("Settings reset to defaults");
        }
        
        private void OnBackButtonClicked()
        {
            gameObject.SetActive(false);
        }
        
        // Helper methods for dropdown indices
        private int GetResolutionIndex(int width, int height)
        {
            if (width == 1920 && height == 1080) return 0;
            if (width == 1280 && height == 720) return 1;
            if (width == 854 && height == 480) return 2;
            if (width == 640 && height == 480) return 3;
            return 1; // Default 720p
        }
        
        private (int, int) GetResolutionFromIndex(int index)
        {
            switch (index)
            {
                case 0: return (1920, 1080);
                case 1: return (1280, 720);
                case 2: return (854, 480);
                case 3: return (640, 480);
                default: return (1280, 720);
            }
        }
        
        private int GetFPSIndex(int fps)
        {
            if (fps == 60) return 0;
            if (fps == 30) return 1;
            if (fps == 20) return 2;
            if (fps == 15) return 3;
            return 1; // Default 30fps
        }
        
        private int GetFPSFromIndex(int index)
        {
            switch (index)
            {
                case 0: return 60;
                case 1: return 30;
                case 2: return 20;
                case 3: return 15;
                default: return 30;
            }
        }
        
        private int GetSamplingRateIndex(float rate)
        {
            if (rate == 200f) return 0;
            if (rate == 100f) return 1;
            if (rate == 50f) return 2;
            if (rate == 25f) return 3;
            return 1; // Default 100Hz
        }
        
        private float GetSamplingRateFromIndex(int index)
        {
            switch (index)
            {
                case 0: return 200f;
                case 1: return 100f;
                case 2: return 50f;
                case 3: return 25f;
                default: return 100f;
            }
        }
    }
}
