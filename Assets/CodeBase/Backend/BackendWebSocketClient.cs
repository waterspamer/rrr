using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

public sealed class BackendWebSocketClient : IDisposable
{
    private readonly BackendConfig config;
    private readonly Action<Action> dispatchToMainThread;
    private ClientWebSocket socket;
    private CancellationTokenSource cancellation;

    public event Action Connected;
    public event Action Closed;
    public event Action<string> RawMessageReceived;
    public event Action<Exception> Error;

    public bool IsConnected => socket != null && socket.State == WebSocketState.Open;

    public BackendWebSocketClient(BackendConfig config, Action<Action> dispatchToMainThread)
    {
        this.config = config;
        this.dispatchToMainThread = dispatchToMainThread;
    }

    public async Task ConnectAsync(string sessionToken)
    {
        await DisconnectAsync();

        socket = new ClientWebSocket();
        cancellation = new CancellationTokenSource();
        Uri uri = new Uri(config.ResolveWebSocketUrl(sessionToken));
        await socket.ConnectAsync(uri, cancellation.Token);
        PostToMainThread(delegate { Connected?.Invoke(); });
        _ = ReceiveLoopAsync(socket, cancellation.Token);
    }

    public async Task DisconnectAsync()
    {
        if (socket == null)
            return;

        try
        {
            if (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived)
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "client_disconnect", CancellationToken.None);
        }
        catch
        {
        }

        cancellation?.Cancel();
        socket.Dispose();
        socket = null;
        cancellation?.Dispose();
        cancellation = null;
        PostToMainThread(delegate { Closed?.Invoke(); });
    }

    public Task SendAsync(object payload)
    {
        string json = JsonUtility.ToJson(payload);
        return SendTextAsync(json);
    }

    public async Task SendTextAsync(string json)
    {
        if (!IsConnected)
            throw new InvalidOperationException("Backend websocket is not connected.");

        byte[] bytes = Encoding.UTF8.GetBytes(json);
        ArraySegment<byte> segment = new ArraySegment<byte>(bytes);
        await socket.SendAsync(segment, WebSocketMessageType.Text, true, cancellation.Token);
    }

    private async Task ReceiveLoopAsync(ClientWebSocket activeSocket, CancellationToken token)
    {
        byte[] buffer = new byte[4096];

        try
        {
            while (!token.IsCancellationRequested && activeSocket.State == WebSocketState.Open)
            {
                using (MemoryStream stream = new MemoryStream())
                {
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await activeSocket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            await DisconnectAsync();
                            return;
                        }

                        stream.Write(buffer, 0, result.Count);
                    }
                    while (!result.EndOfMessage);

                    string json = Encoding.UTF8.GetString(stream.ToArray());
                    PostToMainThread(delegate { RawMessageReceived?.Invoke(json); });
                }
            }
        }
        catch (Exception ex)
        {
            if (!(ex is OperationCanceledException))
                PostToMainThread(delegate { Error?.Invoke(ex); });
        }
    }

    private void PostToMainThread(Action action)
    {
        dispatchToMainThread?.Invoke(action);
    }

    public void Dispose()
    {
        _ = DisconnectAsync();
    }
}
