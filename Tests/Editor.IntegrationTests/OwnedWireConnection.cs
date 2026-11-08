using System.Net;
using System.Net.Sockets;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Editor.IntegrationTests;

/// <summary>Raw lines for malformed/pipelined tests that the sequential protocol client intentionally cannot send.</summary>
internal sealed class OwnedWireConnection : IDisposable
{
    internal OwnedWireConnection(OwnedEditorSession session)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            _tcp.ConnectAsync(IPAddress.Loopback, session.Port, timeout.Token).GetAwaiter().GetResult();
            var stream = _tcp.GetStream();
            stream.ReadTimeout = 10000;
            stream.WriteTimeout = 10000;
            _reader = new StreamReader(stream, new UTF8Encoding(false));
            _writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
            Send(new JObject { ["id"] = "ownership", ["method"] = "getPaths" });
            var reply = Read();
            if (reply["id"]?.Value<string>() != "ownership" || reply["ok"]?.Value<bool>() != true || reply["result"] is not JObject paths)
            {
                throw new InvalidDataException("Raw connection did not acknowledge its ownership request.");
            }
            session.Paths.Verify(paths);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal void Send(params JObject[] requests)
        => SendText(string.Join("\n", requests.Select(request => request.ToString(Formatting.None))) + "\n");

    internal void SendText(string text) => _writer!.Write(text);

    internal JObject Read()
        => JObject.Parse(_reader!.ReadLine() ?? throw new EndOfStreamException("Raw bridge disconnected; pending outcomes may be unknown."));

    internal int ReadTimeout { set => _tcp.GetStream().ReadTimeout = value; }

    public void Dispose()
    {
        _tcp.Dispose();
        _reader?.Dispose();
        _writer?.Dispose();
    }

    private readonly TcpClient _tcp = new();
    private readonly StreamReader? _reader;
    private readonly StreamWriter? _writer;
}
