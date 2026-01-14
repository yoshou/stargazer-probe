using System;
using System.Collections.Generic;
using UnityEngine;
using StargazerProbe.Camera;

namespace StargazerProbe.Sensors
{
    /// <summary>
    /// Accumulates IMU sensor data between camera frames.
    /// When a camera frame arrives, attaches accumulated IMU samples and forwards to next stage.
    /// 
    /// Pipeline: CameraFrameEncoder -> IMUDataAccumulator -> GrpcDataStreamer
    /// </summary>
    public class IMUDataAccumulator : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private IMUSensorManager sensorManager;

        [Header("Settings")]
        [SerializeField] private int maxSamplesPerFrame = 10; // Limit IMU samples per frame

        // Events
        public event Action<CameraFrameDataWithIMU> OnFrameWithIMUReady;

        // Private Fields
        private readonly List<SensorData> accumulatedSamples = new List<SensorData>();
        private readonly object lockObject = new object();
        private bool isAccumulating = false;

        private void Start()
        {
            RefreshReferences();
            SubscribeToEvents();
        }

        private void OnDestroy()
        {
            UnsubscribeFromEvents();
        }

        private void RefreshReferences()
        {
            if (sensorManager == null)
            {
                sensorManager = FindAnyObjectByType<IMUSensorManager>();
            }
        }

        private void SubscribeToEvents()
        {
            if (sensorManager != null)
            {
                sensorManager.OnSensorDataUpdated += OnSensorDataUpdated;
            }
            else
            {
                Debug.LogWarning("[IMUDataAccumulator] IMUSensorManager not found");
            }
        }

        private void UnsubscribeFromEvents()
        {
            if (sensorManager != null)
            {
                sensorManager.OnSensorDataUpdated -= OnSensorDataUpdated;
            }
        }

        private void OnSensorDataUpdated(SensorData data)
        {
            if (!isAccumulating)
                return;

            lock (lockObject)
            {
                accumulatedSamples.Add(data);
                
                // Prevent unbounded growth if frames are not being processed
                if (accumulatedSamples.Count > maxSamplesPerFrame * 10)
                {
                    // Keep only recent samples
                    int excess = accumulatedSamples.Count - maxSamplesPerFrame;
                    accumulatedSamples.RemoveRange(0, excess);
                }
            }
        }

        /// <summary>
        /// Called when a camera frame is encoded and ready.
        /// Attaches accumulated IMU samples and forwards to next stage.
        /// </summary>
        public void ProcessEncodedFrame(CameraFrameData frameData)
        {
            SensorData[] imuSamples;

            lock (lockObject)
            {
                // Copy accumulated samples
                int count = Mathf.Min(accumulatedSamples.Count, maxSamplesPerFrame);
                imuSamples = new SensorData[count];
                
                if (count > 0)
                {
                    // Take most recent samples
                    int startIndex = Mathf.Max(0, accumulatedSamples.Count - count);
                    accumulatedSamples.CopyTo(startIndex, imuSamples, 0, count);
                    
                    // Clear accumulated samples after copying
                    accumulatedSamples.Clear();
                }
            }

            // Create combined data structure
            var frameWithIMU = new CameraFrameDataWithIMU
            {
                FrameData = frameData,
                IMUSamples = imuSamples
            };

            // Forward to next stage
            OnFrameWithIMUReady?.Invoke(frameWithIMU);
        }

        /// <summary>
        /// Enable or disable IMU data accumulation
        /// </summary>
        public void SetAccumulating(bool enabled)
        {
            isAccumulating = enabled;
            
            if (!enabled)
            {
                // Clear accumulated samples when stopping
                lock (lockObject)
                {
                    accumulatedSamples.Clear();
                }
            }
        }
    }

    /// <summary>
    /// Camera frame data combined with accumulated IMU samples
    /// </summary>
    public struct CameraFrameDataWithIMU
    {
        public CameraFrameData FrameData;
        public SensorData[] IMUSamples;
    }
}
