namespace MiHotKeyApp.Ipc;

using System.IO.Pipes;
using System.Text.Json;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Extensions.Logging;

internal sealed class AppCommandPipeServer : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new();

    private readonly string _pipeName;
    private readonly Func<AppCommandRequest, Task<AppCommandResponse>> _handler;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _stop = new();

    private Task? _loopTask;

    public AppCommandPipeServer(
        string pipeName,
        Func<AppCommandRequest, Task<AppCommandResponse>> handler,
        ILogger logger)
    {
        _pipeName = pipeName;
        _handler = handler;
        _logger = logger;
    }

    public void Start()
    {
        _loopTask ??= Task.Run(() => RunAsync(_stop.Token));
    }

    public void Dispose()
    {
        _stop.Cancel();

        if (_loopTask is not null)
        {
            try
            {
                _loopTask.Wait(millisecondsTimeout: 2000);
            }
            catch (AggregateException ex) when (ex.InnerExceptions.All(static e => e is OperationCanceledException))
            {
            }
        }

        _stop.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            using var server = CreateServerStream();

            try
            {
                await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                await HandleClientAsync(server, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ipc server loop failed pipe={pipe}", _pipeName);
                await Task.Delay(250, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private NamedPipeServerStream CreateServerStream()
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
            PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance,
            AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            _pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 0,
            outBufferSize: 0,
            security);
    }

    private async Task HandleClientAsync(NamedPipeServerStream server, CancellationToken cancellationToken)
    {
        try
        {
            var request = await JsonSerializer.DeserializeAsync<AppCommandRequest>(server, JsonOptions, cancellationToken).ConfigureAwait(false);
            var response = request is null
                ? new AppCommandResponse(CommandLine.AppExitCodes.InvalidArguments, "IPC request body was empty.")
                : await _handler(request).ConfigureAwait(false);

            await JsonSerializer.SerializeAsync(server, response, JsonOptions, cancellationToken).ConfigureAwait(false);
            await server.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "ipc request parse failed");
            await TryWriteResponseAsync(
                server,
                new AppCommandResponse(CommandLine.AppExitCodes.InvalidArguments, $"IPC request parse failed: {ex.Message}"),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "ipc request failed");
            await TryWriteResponseAsync(
                server,
                new AppCommandResponse(CommandLine.AppExitCodes.InternalError, "IPC request failed."),
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task TryWriteResponseAsync(
        NamedPipeServerStream server,
        AppCommandResponse response,
        CancellationToken cancellationToken)
    {
        if (!server.IsConnected)
        {
            return;
        }

        try
        {
            await JsonSerializer.SerializeAsync(server, response, JsonOptions, cancellationToken).ConfigureAwait(false);
            await server.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
        }
    }
}
