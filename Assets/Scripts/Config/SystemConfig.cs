using System;
using UnityEngine;

namespace StargazerProbe.Config
{
    /// <summary>
    /// システム設定を管理するクラス（永続化対応）
    /// </summary>
    public class SystemConfig : MonoBehaviour
    {
        private static SystemConfig instance;
        public static SystemConfig Instance
        {
            get
            {
                if (instance == null)
                {
                    GameObject go = new GameObject("SystemConfig");
                    instance = go.AddComponent<SystemConfig>();
                    DontDestroyOnLoad(go);
                }
                return instance;
            }
        }
        
        // 設定データ
        public ServerConfig Server { get; private set; }
        public CameraConfig Camera { get; private set; }
        public SensorConfig Sensor { get; private set; }
        public AdvancedConfig Advanced { get; private set; }
        
        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            
            instance = this;
            DontDestroyOnLoad(gameObject);
            
            LoadSettings();
        }
        
        /// <summary>
        /// 設定を読み込む
        /// </summary>
        public void LoadSettings()
        {
            Server = new ServerConfig
            {
                IpAddress = PlayerPrefs.GetString("Server.IpAddress", "192.168.1.100"),
                Port = PlayerPrefs.GetInt("Server.Port", 50051),
                AutoReconnect = PlayerPrefs.GetInt("Server.AutoReconnect", 1) == 1,
                ReconnectDelay = PlayerPrefs.GetFloat("Server.ReconnectDelay", 3f)
            };
            
            Camera = new CameraConfig
            {
                Width = PlayerPrefs.GetInt("Camera.Width", 1280),
                Height = PlayerPrefs.GetInt("Camera.Height", 720),
                TargetFPS = PlayerPrefs.GetInt("Camera.TargetFPS", 30),
                JpegQuality = PlayerPrefs.GetInt("Camera.JpegQuality", 75)
            };
            
            Sensor = new SensorConfig
            {
                SamplingRate = PlayerPrefs.GetFloat("Sensor.SamplingRate", 100f),
                EnableAccelerometer = PlayerPrefs.GetInt("Sensor.EnableAccelerometer", 1) == 1,
                EnableGyroscope = PlayerPrefs.GetInt("Sensor.EnableGyroscope", 1) == 1,
                EnableMagnetometer = PlayerPrefs.GetInt("Sensor.EnableMagnetometer", 1) == 1
            };
            
            Advanced = new AdvancedConfig
            {
                AutoAdjustQuality = PlayerPrefs.GetInt("Advanced.AutoAdjustQuality", 1) == 1,
                EnableFrameSkip = PlayerPrefs.GetInt("Advanced.EnableFrameSkip", 1) == 1,
                ShowDebugInfo = PlayerPrefs.GetInt("Advanced.ShowDebugInfo", 0) == 1,
                MaxBufferSize = PlayerPrefs.GetInt("Advanced.MaxBufferSize", 100)
            };
            
            Debug.Log("Settings loaded from PlayerPrefs");
        }
        
        /// <summary>
        /// 設定を保存する
        /// </summary>
        public void SaveSettings()
        {
            // Server
            PlayerPrefs.SetString("Server.IpAddress", Server.IpAddress);
            PlayerPrefs.SetInt("Server.Port", Server.Port);
            PlayerPrefs.SetInt("Server.AutoReconnect", Server.AutoReconnect ? 1 : 0);
            PlayerPrefs.SetFloat("Server.ReconnectDelay", Server.ReconnectDelay);
            
            // Camera
            PlayerPrefs.SetInt("Camera.Width", Camera.Width);
            PlayerPrefs.SetInt("Camera.Height", Camera.Height);
            PlayerPrefs.SetInt("Camera.TargetFPS", Camera.TargetFPS);
            PlayerPrefs.SetInt("Camera.JpegQuality", Camera.JpegQuality);
            
            // Sensor
            PlayerPrefs.SetFloat("Sensor.SamplingRate", Sensor.SamplingRate);
            PlayerPrefs.SetInt("Sensor.EnableAccelerometer", Sensor.EnableAccelerometer ? 1 : 0);
            PlayerPrefs.SetInt("Sensor.EnableGyroscope", Sensor.EnableGyroscope ? 1 : 0);
            PlayerPrefs.SetInt("Sensor.EnableMagnetometer", Sensor.EnableMagnetometer ? 1 : 0);
            
            // Advanced
            PlayerPrefs.SetInt("Advanced.AutoAdjustQuality", Advanced.AutoAdjustQuality ? 1 : 0);
            PlayerPrefs.SetInt("Advanced.EnableFrameSkip", Advanced.EnableFrameSkip ? 1 : 0);
            PlayerPrefs.SetInt("Advanced.ShowDebugInfo", Advanced.ShowDebugInfo ? 1 : 0);
            PlayerPrefs.SetInt("Advanced.MaxBufferSize", Advanced.MaxBufferSize);
            
            PlayerPrefs.Save();
            Debug.Log("Settings saved to PlayerPrefs");
        }
        
        /// <summary>
        /// 設定をデフォルトに戻す
        /// </summary>
        public void ResetToDefaults()
        {
            PlayerPrefs.DeleteAll();
            LoadSettings();
            Debug.Log("Settings reset to defaults");
        }
    }
    
    /// <summary>
    /// サーバー設定
    /// </summary>
    [Serializable]
    public class ServerConfig
    {
        public string IpAddress;
        public int Port;
        public bool AutoReconnect;
        public float ReconnectDelay;
    }
    
    /// <summary>
    /// カメラ設定
    /// </summary>
    [Serializable]
    public class CameraConfig
    {
        public int Width;
        public int Height;
        public int TargetFPS;
        public int JpegQuality;
    }
    
    /// <summary>
    /// センサー設定
    /// </summary>
    [Serializable]
    public class SensorConfig
    {
        public float SamplingRate;
        public bool EnableAccelerometer;
        public bool EnableGyroscope;
        public bool EnableMagnetometer;
    }
    
    /// <summary>
    /// 詳細設定
    /// </summary>
    [Serializable]
    public class AdvancedConfig
    {
        public bool AutoAdjustQuality;
        public bool EnableFrameSkip;
        public bool ShowDebugInfo;
        public int MaxBufferSize;
    }
}
