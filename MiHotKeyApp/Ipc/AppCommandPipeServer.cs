namespace MiHotKeyApp.Ipc;

using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

internal sealed class AppCommandPipeServer : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new();

    private readonly int _port;
    private readonly string _authToken;
    private readonly Func<AppCommandRequest, Task<AppCommandResponse>> _handler;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _stop = new();
    private readonly TcpListener _listener;

    private Task? _loopTask;

    public AppCommandPipeServer(
        int port,
        string authToken,
        Func<AppCommandRequest, Task<AppCommandResponse>> handler,
        ILogger logger)
    {
        _port = port;
        _authToken = authToken;
        _handler = handler;
        _logger = logger;
        _listener = new TcpListener(IPAddress.Loopback, port);
    }

    public void Start()
    {
        _listener.Start();
        _loopTask ??= Task.Run(() => RunAsync(_stop.Token));
    }

    public void Dispose()
    {
        _stop.Cancel();

        try
        {
            _listener.Stop();
        }
        catch
        {
        }

        if (_loopTask is not null)
        {
            try
            {
                _loopTask.Wait(millisecondsTimeout: 2000);
            }
            catch (AggregateException ex) when (ex.InnerExceptions.All(static e => e is OperationCanceledException || e is SocketException))
            {
            }
        }

        _stop.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient? client = null;
            try
            {
                client = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                _ = Task.Run(() => HandleClientAsync(client, cancellationToken), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                client?.Dispose();
                break;
            }
            catch (SocketException) when (cancellationToken.IsCancellationRequested)
            {
                client?.Dispose();
                break;
            }
            catch (Exception ex)
            {
                client?.Dispose();
                _logger.LogError(ex, "ipc server loop failed port={port}", _port);
                await Task.Delay(250, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using var _ = client;

        try
        {
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
            using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true)
            {
                AutoFlush = true,
            };

            var requestLine = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(requestLine))
            {
                await writer.WriteLineAsync(JsonSerializer.Serialize(
                    new AppCommandResponse(CommandLine.AppExitCodes.InvalidArguments, "IPC request body was empty."),
                    JsonOptions)).ConfigureAwait(false);
                return;
            }

            var request = JsonSerializer.Deserialize<AppCommandRequest>(requestLine, JsonOptions);
            if (request is null)
            {
                await writer.WriteLineAsync(JsonSerializer.Serialize(
                    new AppCommandResponse(CommandLine.AppExitCodes.InvalidArguments, "IPC request body was empty."),
                    JsonOptions)).ConfigureAwait(false);
                return;
            }

            if (!string.Equals(request.Token, _authToken, StringComparison.Ordinal))
            {
                await writer.WriteLineAsync(JsonSerializer.Serialize(
                    new AppCommandResponse(CommandLine.AppExitCodes.IpcUnavailable, "IPC authentication failed."),
                    JsonOptions)).ConfigureAwait(false);
                return;
            }

            var response = await _handler(request).ConfigureAwait(false);
            await writer.WriteLineAsync(JsonSerializer.Serialize(response, JsonOptions)).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "ipc request parse failed");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ipc request failed");
        }
    }
}
