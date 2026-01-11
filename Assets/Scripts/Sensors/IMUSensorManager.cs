using System;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using InputSystemAccelerometer = UnityEngine.InputSystem.Accelerometer;
using InputSystemGyroscope = UnityEngine.InputSystem.Gyroscope;
using InputSystemMagneticFieldSensor = UnityEngine.InputSystem.MagneticFieldSensor;
using InputSystemGravitySensor = UnityEngine.InputSystem.GravitySensor;
#endif

namespace StargazerProbe.Sensors
{
    /// <summary>
    /// IMUセンサー（加速度、ジャイロ、磁力計）を統合管理するクラス
    /// </summary>
    public class IMUSensorManager : MonoBehaviour
    {
        [Header("Sensor Settings")]
        [SerializeField] private float samplingRate = 100f; // Hz
        
        [Header("Sensor Enable/Disable")]
        [SerializeField] private bool enableAccelerometer = true;
        [SerializeField] private bool enableGyroscope = true;
        [SerializeField] private bool enableMagnetometer = true;
        
        // センサーデータ
        public Vector3 Acceleration { get; private set; }
        public Vector3 Gyroscope { get; private set; }
        public Vector3 Magnetometer { get; private set; }
        public Vector3 Gravity { get; private set; }
        
        // センサー状態
        public bool IsAccelerometerAvailable { get; private set; }
        public bool IsGyroscopeAvailable { get; private set; }
        public bool IsMagnetometerAvailable { get; private set; }
        
        // イベント
        public event Action<SensorData> OnSensorDataUpdated;
        
        private float updateInterval;
        private float lastUpdateTime;

    #if ENABLE_INPUT_SYSTEM
        private InputSystemAccelerometer accelerometer;
        private InputSystemGyroscope gyroscope;
        private InputSystemMagneticFieldSensor magneticFieldSensor;
        private InputSystemGravitySensor gravitySensor;
    #endif
        
        private void Awake()
        {
            updateInterval = 1f / samplingRate;
        }
        
        private void Start()
        {
            InitializeSensors();
        }
        
        private void InitializeSensors()
        {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            // Input System API
            accelerometer = InputSystemAccelerometer.current;
            gyroscope = InputSystemGyroscope.current;
            magneticFieldSensor = InputSystemMagneticFieldSensor.current;
            gravitySensor = InputSystemGravitySensor.current;

            IsAccelerometerAvailable = enableAccelerometer && accelerometer != null;
            IsGyroscopeAvailable = enableGyroscope && gyroscope != null;
            IsMagnetometerAvailable = enableMagnetometer && magneticFieldSensor != null;

            if (IsAccelerometerAvailable)
            {
                InputSystem.EnableDevice(accelerometer);
                TrySetSamplingFrequency(accelerometer, samplingRate);
            }
            else if (enableAccelerometer)
            {
                Debug.LogWarning("Accelerometer is not available (Input System)");
            }

            if (IsGyroscopeAvailable)
            {
                InputSystem.EnableDevice(gyroscope);
                TrySetSamplingFrequency(gyroscope, samplingRate);
                Debug.Log("Gyroscope initialized (Input System)");
            }
            else if (enableGyroscope)
            {
                Debug.LogWarning("Gyroscope is not available (Input System)");
            }

            if (IsMagnetometerAvailable)
            {
                InputSystem.EnableDevice(magneticFieldSensor);
                TrySetSamplingFrequency(magneticFieldSensor, samplingRate);
                Debug.Log("Magnetometer initialized (Input System)");
            }
            else if (enableMagnetometer)
            {
                Debug.LogWarning("Magnetometer is not available (Input System)");
            }

            if (gravitySensor != null)
            {
                InputSystem.EnableDevice(gravitySensor);
                TrySetSamplingFrequency(gravitySensor, samplingRate);
            }

            Debug.Log($"IMU Sensors initialized (Input System) - Sampling Rate: {samplingRate}Hz");
#else
            // Legacy Input API
            // 加速度センサー
            if (enableAccelerometer)
            {
                IsAccelerometerAvailable = SystemInfo.supportsAccelerometer;
                if (!IsAccelerometerAvailable)
                {
                    Debug.LogWarning("Accelerometer is not available on this device");
                }
            }
            
            // ジャイロスコープ
            if (enableGyroscope)
            {
                IsGyroscopeAvailable = SystemInfo.supportsGyroscope;
                if (IsGyroscopeAvailable)
                {
                    Input.gyro.enabled = true;
                    Debug.Log("Gyroscope initialized");
                }
                else
                {
                    Debug.LogWarning("Gyroscope is not available on this device");
                }
            }
            
            // 磁力計（コンパス）
            if (enableMagnetometer)
            {
                Input.compass.enabled = true;
                IsMagnetometerAvailable = true;
                Debug.Log("Magnetometer initialized");
            }
            
            Debug.Log($"IMU Sensors initialized - Sampling Rate: {samplingRate}Hz");
#endif
        }
        
        private void Update()
        {
            // 指定されたサンプリングレートでデータを更新
            if (Time.time - lastUpdateTime >= updateInterval)
            {
                UpdateSensorData();
                lastUpdateTime = Time.time;
            }
        }
        
        private void UpdateSensorData()
        {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            // 加速度
            if (enableAccelerometer && IsAccelerometerAvailable)
            {
                Acceleration = accelerometer.acceleration.ReadValue();
            }

            // ジャイロ（rad/s）
            if (enableGyroscope && IsGyroscopeAvailable)
            {
                Gyroscope = gyroscope.angularVelocity.ReadValue();
            }

            // 磁場
            if (enableMagnetometer && IsMagnetometerAvailable)
            {
                Magnetometer = magneticFieldSensor.magneticField.ReadValue();
            }

            // 重力
            if (gravitySensor != null)
            {
                Gravity = gravitySensor.gravity.ReadValue();
            }
            else if (IsAccelerometerAvailable)
            {
                Gravity = Acceleration.sqrMagnitude > 0.0001f ? Acceleration.normalized * 9.81f : Vector3.zero;
            }
#else
            // Legacy Input API
            if (enableAccelerometer && IsAccelerometerAvailable)
            {
                Acceleration = Input.acceleration;
            }
            
            if (enableGyroscope && IsGyroscopeAvailable)
            {
                Gyroscope = Input.gyro.rotationRateUnbiased;
            }
            
            if (enableMagnetometer && IsMagnetometerAvailable)
            {
                Magnetometer = Input.compass.rawVector;
            }
            
            if (IsGyroscopeAvailable)
            {
                Gravity = Input.gyro.gravity;
            }
            else if (IsAccelerometerAvailable)
            {
                Gravity = Acceleration.normalized * 9.81f;
            }
#endif
            
            // イベント発火
            OnSensorDataUpdated?.Invoke(new SensorData
            {
                Timestamp = Time.realtimeSinceStartup,
                Acceleration = Acceleration,
                Gyroscope = Gyroscope,
                Magnetometer = Magnetometer,
                Gravity = Gravity
            });
        }
        
        /// <summary>
        /// サンプリングレートを動的に変更
        /// </summary>
        public void SetSamplingRate(float rate)
        {
            samplingRate = Mathf.Clamp(rate, 1f, 200f);
            updateInterval = 1f / samplingRate;

#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            if (accelerometer != null) TrySetSamplingFrequency(accelerometer, samplingRate);
            if (gyroscope != null) TrySetSamplingFrequency(gyroscope, samplingRate);
            if (magneticFieldSensor != null) TrySetSamplingFrequency(magneticFieldSensor, samplingRate);
            if (gravitySensor != null) TrySetSamplingFrequency(gravitySensor, samplingRate);
#endif
            Debug.Log($"Sampling rate changed to {samplingRate}Hz");
        }
        
        /// <summary>
        /// センサーの有効/無効を切り替え
        /// </summary>
        public void SetSensorEnabled(SensorType sensorType, bool enabled)
        {
            switch (sensorType)
            {
                case SensorType.Accelerometer:
                    enableAccelerometer = enabled;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
                    if (accelerometer != null)
                    {
                        if (enabled) InputSystem.EnableDevice(accelerometer);
                        else InputSystem.DisableDevice(accelerometer);
                    }
#else
                    // No explicit enable toggle for legacy accelerometer.
#endif
                    break;
                case SensorType.Gyroscope:
                    enableGyroscope = enabled;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
                    if (gyroscope != null)
                    {
                        if (enabled) InputSystem.EnableDevice(gyroscope);
                        else InputSystem.DisableDevice(gyroscope);
                    }
#else
                    if (IsGyroscopeAvailable)
                    {
                        Input.gyro.enabled = enabled;
                    }
#endif
                    break;
                case SensorType.Magnetometer:
                    enableMagnetometer = enabled;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
                    if (magneticFieldSensor != null)
                    {
                        if (enabled) InputSystem.EnableDevice(magneticFieldSensor);
                        else InputSystem.DisableDevice(magneticFieldSensor);
                    }
#else
                    Input.compass.enabled = enabled;
#endif
                    break;
            }
        }
        
        private void OnDestroy()
        {
            // センサーのクリーンアップ
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            if (accelerometer != null) InputSystem.DisableDevice(accelerometer);
            if (gyroscope != null) InputSystem.DisableDevice(gyroscope);
            if (magneticFieldSensor != null) InputSystem.DisableDevice(magneticFieldSensor);
            if (gravitySensor != null) InputSystem.DisableDevice(gravitySensor);
#else
            if (Input.gyro.enabled)
            {
                Input.gyro.enabled = false;
            }
            if (Input.compass.enabled)
            {
                Input.compass.enabled = false;
            }
#endif
        }

#if ENABLE_INPUT_SYSTEM
        private static void TrySetSamplingFrequency(Sensor sensor, float hz)
        {
            if (sensor == null) return;
            try
            {
                sensor.samplingFrequency = hz;
            }
            catch
            {
                // Some sensors/devices do not allow changing sampling frequency.
            }
        }
#endif
    }
    
    /// <summary>
    /// センサーデータ構造体
    /// </summary>
    [Serializable]
    public struct SensorData
    {
        public double Timestamp;
        public Vector3 Acceleration;
        public Vector3 Gyroscope;
        public Vector3 Magnetometer;
        public Vector3 Gravity;
    }
    
    /// <summary>
    /// センサータイプ列挙型
    /// </summary>
    public enum SensorType
    {
        Accelerometer,
        Gyroscope,
        Magnetometer
    }
}
