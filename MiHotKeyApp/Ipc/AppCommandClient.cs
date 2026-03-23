namespace MiHotKeyApp.Ipc;

using System.IO.Pipes;
using System.Text.Json;
using System.Security.Principal;
using MiHotKeyApp.CommandLine;

internal static class AppCommandClient
{
    private static readonly JsonSerializerOptions JsonOptions = new();

    public static AppCommandResponse Send(string pipeName, AppCommandRequest request)
    {
        try
        {
            using var client = new NamedPipeClientStream(
                ".",
                pipeName,
                PipeDirection.InOut,
                PipeOptions.None,
                TokenImpersonationLevel.Impersonation);
            client.Connect(timeout: 3000);

            JsonSerializer.Serialize(client, request, JsonOptions);
            client.Flush();

            var response = JsonSerializer.Deserialize<AppCommandResponse>(client, JsonOptions);
            return response ?? new AppCommandResponse(AppExitCodes.InternalError, "IPC returned no response.");
        }
        catch (TimeoutException)
        {
            return new AppCommandResponse(AppExitCodes.IpcUnavailable, "MiHotKey is running but the IPC endpoint did not respond in time.");
        }
        catch (IOException ex)
        {
            return new AppCommandResponse(AppExitCodes.IpcUnavailable, $"IPC transport failed: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            return new AppCommandResponse(AppExitCodes.IpcUnavailable, $"IPC access denied: {ex.Message}");
        }
        catch (JsonException ex)
        {
            return new AppCommandResponse(AppExitCodes.InternalError, $"IPC protocol error: {ex.Message}");
        }
    }
}
