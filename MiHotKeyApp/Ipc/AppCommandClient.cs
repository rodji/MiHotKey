namespace MiHotKeyApp.Ipc;

using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using MiHotKeyApp.CommandLine;

internal static class AppCommandClient
{
    private static readonly JsonSerializerOptions JsonOptions = new();

    public static AppCommandClientResult Send(int port, AppCommandRequest request)
    {
        try
        {
            using var client = new TcpClient(AddressFamily.InterNetwork);
            client.Connect(IPAddress.Loopback, port);

            using var stream = client.GetStream();
            stream.ReadTimeout = 3000;
            stream.WriteTimeout = 3000;
            using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true)
            {
                AutoFlush = true,
            };
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);

            writer.WriteLine(JsonSerializer.Serialize(request, JsonOptions));
            var responseLine = reader.ReadLine();
            if (string.IsNullOrWhiteSpace(responseLine))
            {
                return ProtocolFailure("IPC returned no response.");
            }

            var response = JsonSerializer.Deserialize<AppCommandResponse>(responseLine, JsonOptions);
            return response is null
                ? ProtocolFailure("IPC returned no response.")
                : new AppCommandClientResult(AppCommandClientStatus.Success, response);
        }
        catch (SocketException ex)
        {
            return TransportFailure($"IPC transport failed: {ex.Message}");
        }
        catch (IOException ex)
        {
            return TransportFailure($"IPC transport failed: {ex.Message}");
        }
        catch (JsonException ex)
        {
            return ProtocolFailure($"IPC protocol error: {ex.Message}");
        }
    }

    public static AppCommandClientResult Ping(int port, string token)
    {
        return Send(port, new AppCommandRequest(AppCommandRequest.PingCommand, "", token));
    }

    private static AppCommandClientResult TransportFailure(string message)
    {
        return new AppCommandClientResult(
            AppCommandClientStatus.TransportFailure,
            new AppCommandResponse(AppExitCodes.IpcUnavailable, message));
    }

    private static AppCommandClientResult ProtocolFailure(string message)
    {
        return new AppCommandClientResult(
            AppCommandClientStatus.ProtocolFailure,
            new AppCommandResponse(AppExitCodes.InternalError, message));
    }
}
