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
        private readonly SynchronizationContext unityContext;

        private GrpcChannel channel;
        private Stargazer.SensorStream.SensorStreamClient client;
        private AsyncDuplexStreamingCall<Stargazer.DataPacket, Stargazer.DataResponse> call;
        private CancellationTokenSource cts;

        public GrpcConnectionState State { get; private set; } = GrpcConnectionState.Disconnected;
        public bool IsConnected => State == GrpcConnectionState.Connected && call != null;

        public event Action<GrpcConnectionState> OnStateChanged;
        public event Action<Stargazer.DataResponse> OnResponse;
        public event Action<string> OnError;

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

                client = new Stargazer.SensorStream.SensorStreamClient(channel);
                call = client.StreamData(cancellationToken: cts.Token);

                // Handshake: send one tiny packet and wait briefly for a response.
                // This proves HTTP/2 + gRPC stream is actually established (TCP reachability alone is not enough).
                var handshakePacket = new Stargazer.DataPacket
                {
                    Timestamp = 0,
                    DeviceId = "handshake"
                };

                await call.RequestStream.WriteAsync(handshakePacket).ConfigureAwait(false);

                var moveNextTask = call.ResponseStream.MoveNext(cts.Token);
                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(3), cts.Token);
                var completed = await Task.WhenAny(moveNextTask, timeoutTask).ConfigureAwait(false);
                if (completed != moveNextTask)
                {
                    throw new TimeoutException("Handshake timeout (no response from server)");
                }

                // Consume first response so the background loop doesn't re-emit it.
                if (moveNextTask.Result)
                {
                    var first = call.ResponseStream.Current;
                    if (first != null)
                    {
                        PostToUnity(() => OnResponse?.Invoke(first));
                    }
                }

                SetState(GrpcConnectionState.Connected);
                _ = Task.Run(() => ReadResponsesLoopAsync(call.ResponseStream, cts.Token), cts.Token);
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

        public async Task SendAsync(Stargazer.DataPacket packet)
        {
            if (call == null)
                throw new InvalidOperationException("gRPC stream is not established");

            await call.RequestStream.WriteAsync(packet).ConfigureAwait(false);
        }

        private async Task ReadResponsesLoopAsync(IAsyncStreamReader<Stargazer.DataResponse> responseStream, CancellationToken cancellationToken)
        {
            try
            {
                while (await responseStream.MoveNext(cancellationToken).ConfigureAwait(false))
                {
                    var response = responseStream.Current;
                    if (response == null)
                        continue;

                    PostToUnity(() => OnResponse?.Invoke(response));
                }
            }
            catch (OperationCanceledException)
            {
                // normal
            }
            catch (Exception ex)
            {
                // If we are cancelling (Stop/Dispose), don't treat as an error.
                if (!cancellationToken.IsCancellationRequested)
                {
                    EmitError($"Response loop error: {ex.Message}");
                }
            }
            finally
            {
                SetState(GrpcConnectionState.Disconnected);
            }
        }

        public async Task DisconnectAsync()
        {
            if (State == GrpcConnectionState.Disconnected)
                return;

            try
            {
                cts?.Cancel();

                if (call != null)
                {
                    try { await call.RequestStream.CompleteAsync().ConfigureAwait(false); }
                    catch { /* ignore */ }

                    call.Dispose();
                    call = null;
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
