using System;
using UnityEngine;

namespace StargazerProbe.Sync
{
    /// <summary>
    /// 時刻同期を管理するクラス
    /// センサーとカメラの統一タイムスタンプを提供
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
            
            // Unix エポック（1970/1/1 00:00:00 UTC）
            DateTime epochStart = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            unixEpoch = (DateTime.UtcNow - epochStart).TotalSeconds;
            
            Debug.Log($"TimeSyncManager initialized - Unix time: {unixEpoch:F3}");
        }
        
        /// <summary>
        /// 現在のUnixタイムスタンプを取得（秒）
        /// </summary>
        public double GetCurrentTimestamp()
        {
            double elapsed = Time.realtimeSinceStartupAsDouble - startTime;
            return unixEpoch + elapsed;
        }
        
        /// <summary>
        /// アプリ起動からの相対時刻を取得（秒）
        /// </summary>
        public double GetRelativeTimestamp()
        {
            return Time.realtimeSinceStartupAsDouble;
        }
        
        /// <summary>
        /// 高精度タイムスタンプ（ミリ秒）
        /// </summary>
        public long GetTimestampMilliseconds()
        {
            return (long)(GetCurrentTimestamp() * 1000.0);
        }
        
        /// <summary>
        /// 高精度タイムスタンプ（マイクロ秒）
        /// </summary>
        public long GetTimestampMicroseconds()
        {
            return (long)(GetCurrentTimestamp() * 1000000.0);
        }
        
        /// <summary>
        /// 現在のUTC時刻を取得
        /// </summary>
        public DateTime GetCurrentUTCTime()
        {
            return DateTime.UtcNow;
        }
        
        /// <summary>
        /// タイムスタンプをDateTimeに変換
        /// </summary>
        public DateTime TimestampToDateTime(double timestamp)
        {
            DateTime epochStart = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            return epochStart.AddSeconds(timestamp);
        }
        
        /// <summary>
        /// 2つのタイムスタンプの差分を計算（ミリ秒）
        /// </summary>
        public double GetTimeDifferenceMs(double timestamp1, double timestamp2)
        {
            return Math.Abs(timestamp1 - timestamp2) * 1000.0;
        }
    }
}
