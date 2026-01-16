using System;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Net.Http;
using Grpc.Core;
using Grpc.Net.Client;
using UnityEngine;

namespace StargazerProbe.Grpc
{
    public enum GrpcConnectionState
    {
        Disconnected,
        Connecting,
        Connected
    }

    public sealed class GrpcStreamClient : IDisposable
    {
        // Public Properties
        public GrpcConnectionState State { get; private set; } = GrpcConnectionState.Disconnected;
        public bool IsConnected => State == GrpcConnectionState.Connected && cameraImageCall != null;

        // Events
        public event Action<GrpcConnectionState> OnStateChanged;
        public event Action<string> OnError;

        // Private Fields - Context
        private readonly SynchronizationContext unityContext;

        // Private Fields - gRPC
        private GrpcChannel channel;
        private Stargazer.Sensor.SensorClient client;
        private AsyncClientStreamingCall<Stargazer.CameraImageMessage, Google.Protobuf.WellKnownTypes.Empty> cameraImageCall;
        private AsyncClientStreamingCall<Stargazer.InertialMessage, Google.Protobuf.WellKnownTypes.Empty> inertialCall;
        private CancellationTokenSource cts;

        public GrpcStreamClient(SynchronizationContext unityContext)
        {
            this.unityContext = unityContext;
        }

        public async Task ConnectAsync(string host, int port, CancellationToken externalCancellationToken)
        {
            if (State != GrpcConnectionState.Disconnected)
                return;

            SetState(GrpcConnectionState.Connecting);

            // Allow h2c (HTTP/2 without TLS). Required for "http://" gRPC.
            AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

            cts = CancellationTokenSource.CreateLinkedTokenSource(externalCancellationToken);

            try
            {
                var address = new Uri($"http://{host}:{port}");

                Debug.Log($"[GrpcStreamClient] Connecting to {address}");

                HttpMessageHandler httpHandler = CreateBestHttpHandlerOrThrow(address);
                if (Debug.isDebugBuild)
                {
                    Debug.Log($"[GrpcStreamClient] Using HttpHandler: {httpHandler.GetType().FullName}");
                }

                channel = GrpcChannel.ForAddress(address, new GrpcChannelOptions
                {
                    HttpHandler = httpHandler
                });

                client = new Stargazer.Sensor.SensorClient(channel);
                cameraImageCall = client.PublishCameraImage(cancellationToken: cts.Token);
                inertialCall = client.PublishInertial(cancellationToken: cts.Token);

                // Handshake: send one tiny message to verify stream establishment
                var handshakeCamera = new Stargazer.CameraImageMessage
                {
                    Timestamp = 0,
                    Name = "handshake"
                };

                await cameraImageCall.RequestStream.WriteAsync(handshakeCamera).ConfigureAwait(false);

                // Brief delay to ensure stream is established
                await Task.Delay(TimeSpan.FromMilliseconds(100), cts.Token).ConfigureAwait(false);

                SetState(GrpcConnectionState.Connected);
            }
            catch (Exception ex)
            {
                EmitError($"Connect failed: {ex.Message}");
                await DisconnectAsync();
                throw;
            }
        }

        private static HttpMessageHandler CreateBestHttpHandlerOrThrow(Uri address)
        {
            // Unity/Android: prefer YetAnotherHttpHandler, which provides reliable HTTP/2 support for grpc-dotnet.
            if (Application.platform == RuntimePlatform.Android)
            {
                // gRPC needs HTTP/2. For insecure (http://) endpoints, force h2c by setting Http2Only=true.
                // For https:// endpoints, HTTP/2 will be negotiated via ALPN automatically.
                var handler = new YetAnotherHttpHandler
                {
                    Http2Only = string.Equals(address.Scheme, "http", StringComparison.OrdinalIgnoreCase)
                };
                return handler;
            }

            // Try System.Net.Http.SocketsHttpHandler if available (best chance for HTTP/2 on Unity/Android).
            Type socketsHandlerType = Type.GetType("System.Net.Http.SocketsHttpHandler, System.Net.Http", throwOnError: false);
            if (socketsHandlerType != null)
            {
                object instance = Activator.CreateInstance(socketsHandlerType);
                if (instance is HttpMessageHandler handler)
                {
                    // Try to set EnableMultipleHttp2Connections=true when the property exists.
                    PropertyInfo prop = socketsHandlerType.GetProperty("EnableMultipleHttp2Connections", BindingFlags.Instance | BindingFlags.Public);
                    if (prop != null && prop.CanWrite && prop.PropertyType == typeof(bool))
                    {
                        prop.SetValue(handler, true);
                    }
                    return handler;
                }
            }

            // HttpClientHandler often lacks HTTP/2 in some Unity profiles; fail fast with a clear message.
            throw new NotSupportedException(
                "SocketsHttpHandler is not available in this Unity scripting runtime. " +
                "gRPC over Grpc.Net.Client requires HTTP/2. " +
                "In Unity 6, set Api Compatibility Level to .NET Standard 2.1 (the highest option), then recompile. " +
                "If it still isn't available, switch to gRPC-Web (HTTP/1.1) via Grpc.Net.Client.Web + grpcwebproxy/Envoy on the server.");
        }

        public async Task SendCameraImageAsync(Stargazer.CameraImageMessage message)
        {
            if (cameraImageCall == null)
                throw new InvalidOperationException("gRPC camera stream is not established");

            await cameraImageCall.RequestStream.WriteAsync(message).ConfigureAwait(false);
        }

        public async Task SendInertialAsync(Stargazer.InertialMessage message)
        {
            if (inertialCall == null)
                throw new InvalidOperationException("gRPC inertial stream is not established");

            await inertialCall.RequestStream.WriteAsync(message).ConfigureAwait(false);
        }

        public async Task DisconnectAsync()
        {
            if (State == GrpcConnectionState.Disconnected)
                return;

            try
            {
                cts?.Cancel();

                if (cameraImageCall != null)
                {
                    try { await cameraImageCall.RequestStream.CompleteAsync().ConfigureAwait(false); }
                    catch { /* ignore */ }

                    cameraImageCall.Dispose();
                    cameraImageCall = null;
                }

                if (inertialCall != null)
                {
                    try { await inertialCall.RequestStream.CompleteAsync().ConfigureAwait(false); }
                    catch { /* ignore */ }

                    inertialCall.Dispose();
                    inertialCall = null;
                }

                channel?.Dispose();
                channel = null;
                client = null;
            }
            finally
            {
                cts?.Dispose();
                cts = null;
                SetState(GrpcConnectionState.Disconnected);
            }
        }

        private void SetState(GrpcConnectionState newState)
        {
            if (State == newState)
                return;

            State = newState;
            PostToUnity(() => OnStateChanged?.Invoke(newState));
        }

        private void EmitError(string message)
        {
            Debug.LogError($"[GrpcStreamClient] {message}");
            PostToUnity(() => OnError?.Invoke(message));
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

            // Fallback (shouldn't happen in Unity main thread construction)
            action();
        }

        public void Dispose()
        {
            _ = DisconnectAsync();
        }
    }
}
