using System;
using UnityEngine;

namespace StargazerProbe.Sync
{
    /// <summary>
    /// Manages time synchronization
    /// Provides unified timestamps for sensors and camera
    /// </summary>
    public class TimeSyncManager : MonoBehaviour
    {
        private static TimeSyncManager instance;
        public static TimeSyncManager Instance
        {
            get
            {
                if (instance == null)
                {
                    GameObject go = new GameObject("TimeSyncManager");
                    instance = go.AddComponent<TimeSyncManager>();
                    DontDestroyOnLoad(go);
                }
                return instance;
            }
        }
        
        private double startTime;
        private double unixEpoch;
        
        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            
            instance = this;
            DontDestroyOnLoad(gameObject);
            
            Initialize();
        }
        
        private void Initialize()
        {
            startTime = Time.realtimeSinceStartupAsDouble;
            
            // Unix epoch (1970/1/1 00:00:00 UTC)
            DateTime epochStart = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            unixEpoch = (DateTime.UtcNow - epochStart).TotalSeconds;
            
            Debug.Log($"TimeSyncManager initialized - Unix time: {unixEpoch:F3}");
        }
        
        /// <summary>
        /// Get current Unix timestamp (seconds)
        /// </summary>
        public double GetCurrentTimestamp()
        {
            double elapsed = Time.realtimeSinceStartupAsDouble - startTime;
            return unixEpoch + elapsed;
        }
        
        /// <summary>
        /// Get relative time since app start (seconds)
        /// </summary>
        public double GetRelativeTimestamp()
        {
            return Time.realtimeSinceStartupAsDouble;
        }
        
        /// <summary>
        /// High-precision timestamp (milliseconds)
        /// </summary>
        public long GetTimestampMilliseconds()
        {
            return (long)(GetCurrentTimestamp() * 1000.0);
        }
        
        /// <summary>
        /// High-precision timestamp (microseconds)
        /// </summary>
        public long GetTimestampMicroseconds()
        {
            return (long)(GetCurrentTimestamp() * 1000000.0);
        }
        
        /// <summary>
        /// Get current UTC time
        /// </summary>
        public DateTime GetCurrentUTCTime()
        {
            return DateTime.UtcNow;
        }
        
        /// <summary>
        /// Convert timestamp to DateTime
        /// </summary>
        public DateTime TimestampToDateTime(double timestamp)
        {
            DateTime epochStart = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            return epochStart.AddSeconds(timestamp);
        }
        
        /// <summary>
        /// Calculate time difference between two timestamps (milliseconds)
        /// </summary>
        public double GetTimeDifferenceMs(double timestamp1, double timestamp2)
        {
            return Math.Abs(timestamp1 - timestamp2) * 1000.0;
        }
    }
}
