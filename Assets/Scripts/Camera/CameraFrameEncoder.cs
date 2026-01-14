using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace StargazerProbe.Camera
{
    public sealed class CameraFrameEncoder : IDisposable
    {
        // Public Properties
        public int PendingCount => Volatile.Read(ref pendingEncodes);

        // Events
        public event Action<CameraFrameData> OnFrameEncoded;

        // Private Fields - Queue
        private readonly ConcurrentQueue<EncodeJob> encodeQueue = new ConcurrentQueue<EncodeJob>();
        private readonly SemaphoreSlim encodeSignal = new SemaphoreSlim(0);
        private readonly SynchronizationContext unityContext;

        // Private Fields - Encoding Task
        private CancellationTokenSource encodeCts;
        private Task[] encodeTasks;
        private int pendingEncodes;

        public CameraFrameEncoder(SynchronizationContext unityContext = null)
        {
            this.unityContext = unityContext ?? SynchronizationContext.Current;
        }

        public void Start()
        {
            if (encodeTasks != null)
            {
                bool anyRunning = false;
                foreach (var t in encodeTasks)
                {
                    if (t != null && !t.IsCompleted)
                    {
                        anyRunning = true;
                        break;
                    }
                }
                if (anyRunning) return;
            }

            encodeCts?.Cancel();
            encodeCts?.Dispose();
            encodeCts = new CancellationTokenSource();

            int workerCount = Mathf.Clamp(SystemInfo.processorCount - 1, 1, 4);
            encodeTasks = new Task[workerCount];
            for (int i = 0; i < workerCount; i++)
            {
                encodeTasks[i] = Task.Run(() => EncodeLoopAsync(encodeCts.Token));
            }
        }

        public void Stop()
        {
            try
            {
                encodeCts?.Cancel();
            }
            catch { }

            try
            {
                if (encodeTasks != null)
                {
                    Task.WaitAll(encodeTasks, 200);
                }
            }
            catch { }

            while (encodeQueue.TryDequeue(out var job))
            {
                job.ReturnBufferCallback?.Invoke(job.Pixels);
                Interlocked.Decrement(ref pendingEncodes);
            }

            encodeCts?.Dispose();
            encodeCts = null;
            encodeTasks = null;
        }

        public bool TryEnqueue(double timestamp, int width, int height, int quality, Color32[] pixels, CameraIntrinsics intrinsics, Action<Color32[]> returnBufferCallback)
        {
            if (pixels == null || pixels.Length == 0)
            {
                returnBufferCallback?.Invoke(pixels);
                return false;
            }

            if (encodeCts == null || encodeCts.IsCancellationRequested)
            {
                returnBufferCallback?.Invoke(pixels);
                return false;
            }

            Interlocked.Increment(ref pendingEncodes);

            encodeQueue.Enqueue(new EncodeJob
            {
                Timestamp = timestamp,
                Width = width,
                Height = height,
                Quality = quality,
                Pixels = pixels,
                Intrinsics = intrinsics,
                ReturnBufferCallback = returnBufferCallback
            });

            encodeSignal.Release();
            return true;
        }

        private async Task EncodeLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await encodeSignal.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                if (!encodeQueue.TryDequeue(out var job))
                    continue;

                try
                {
                    byte[] jpegData = ImageConversion.EncodeArrayToJPG(
                        job.Pixels,
                        GraphicsFormat.R8G8B8A8_UNorm,
                        (uint)job.Width,
                        (uint)job.Height,
                        0,
                        job.Quality);

                    var frameData = new CameraFrameData
                    {
                        Timestamp = job.Timestamp,
                        ImageData = jpegData,
                        Width = job.Width,
                        Height = job.Height,
                        Quality = job.Quality,
                        Intrinsics = job.Intrinsics
                    };

                    PostToUnity(() => OnFrameEncoded?.Invoke(frameData));
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[CameraFrameEncoder] Encode failed: {ex.Message}");
                }
                finally
                {
                    job.ReturnBufferCallback?.Invoke(job.Pixels);
                    Interlocked.Decrement(ref pendingEncodes);
                }
            }
        }

        private void PostToUnity(Action action)
        {
            if (action == null)
                return;

            if (unityContext != null)
            {
                unityContext.Post(_ => action(), null);
                return;
            }

            action();
        }

        public void Dispose()
        {
            Stop();
            encodeSignal?.Dispose();
        }

        private struct EncodeJob
        {
            public double Timestamp;
            public int Width;
            public int Height;
            public int Quality;
            public Color32[] Pixels;
            public CameraIntrinsics Intrinsics;
            public Action<Color32[]> ReturnBufferCallback;
        }
    }
}
