using System.Net.Sockets;
using System.Text;
using System.Diagnostics;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TiXL.DebugClient;

/// <summary>
/// Typed client for TiXL's JSON-lines debug protocol (editor started with
/// <c>--debug-server &lt;port&gt;</c>). One request/response at a time per client;
/// strict mode also validates the echoed ID and envelope. Thread-unsafe by design - use one
/// client per driving thread.
/// </summary>
public sealed class DebugProtocolClient : IDisposable
{
    public static bool TryConnect(int port, TimeSpan timeout, out DebugProtocolClient? client,
                                  bool strictResponses = false, TimeSpan? responseTimeout = null)
    {
        var replyTimeout = responseTimeout ?? TimeSpan.FromMinutes(5);
        if (timeout <= TimeSpan.Zero || replyTimeout.TotalMilliseconds < 1 || replyTimeout.TotalMilliseconds > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "Connection and response timeouts must be positive and bounded.");
        }
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < timeout)
        {
            var tcpClient = new TcpClient();
            try
            {
                var remaining = timeout - timer.Elapsed;
                using var cancellation = new CancellationTokenSource(remaining > TimeSpan.Zero ? remaining : TimeSpan.FromMilliseconds(1));
                tcpClient.ConnectAsync("127.0.0.1", port, cancellation.Token).AsTask().GetAwaiter().GetResult();
                client = new DebugProtocolClient(tcpClient, strictResponses, (int)replyTimeout.TotalMilliseconds);
                return true;
            }
            catch (Exception e) when (e is SocketException or OperationCanceledException)
            {
                tcpClient.Dispose();
                var remaining = timeout - timer.Elapsed;
                if (remaining > TimeSpan.Zero)
                {
                    Thread.Sleep(TimeSpan.FromMilliseconds(Math.Min(100, remaining.TotalMilliseconds)));
                }
            }
        }

        client = null;
        return false;
    }

    private DebugProtocolClient(TcpClient tcpClient, bool strictResponses, int responseTimeoutMs)
    {
        _tcpClient = tcpClient;
        _strictResponses = strictResponses;
        var stream = tcpClient.GetStream();
        _reader = new StreamReader(stream, Encoding.UTF8);
        _writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
        _tcpClient.ReceiveTimeout = responseTimeoutMs;
        _tcpClient.SendTimeout = responseTimeoutMs;
    }

    /// <summary>Sends a request and blocks for the response envelope.</summary>
    public Response Call(string method, object? args = null)
    {
        var envelope = CallRaw(method, args);
        return new Response(envelope["ok"]?.Value<bool>() ?? false,
                            envelope["result"] as JObject,
                            envelope["error"]?["code"]?.Value<string>(),
                            envelope["error"]?["detail"]?.Value<string>(),
                            envelope["frame"]?.Value<long>() ?? -1,
                            envelope["structureVersion"]?.Value<int>() ?? -1)
               { Envelope = envelope };
    }

    /// <summary>Returns every wire field. A failed strict exchange closes the connection; it never retries the request.</summary>
    public JObject CallRaw(string method, object? args = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var request = args == null ? new JObject() : JObject.FromObject(args);
        request["id"] = (++_requestCounter).ToString();
        request["method"] = method;

        try
        {
            _writer.WriteLine(request.ToString(Formatting.None));
            var line = _reader.ReadLine() ?? throw new IOException("Debug protocol connection closed; request outcome may be unknown.");
            var envelope = JObject.Parse(line);
            if (_strictResponses)
            {
                ValidateEnvelope(envelope, request["id"]!);
            }
            return envelope;
        }
        catch (Exception e) when (e is IOException or JsonException or DebugProtocolException)
        {
            if (_strictResponses)
            {
                // Reusing a timed-out stream could attribute a late mutation reply to a later request.
                Dispose();
            }
            throw;
        }
    }

    private static void ValidateEnvelope(JObject envelope, JToken requestId)
    {
        if (!JToken.DeepEquals(envelope["id"], requestId))
        {
            throw new DebugProtocolException("Response ID does not match the request; outcome may be unknown.");
        }
        if (envelope["ok"]?.Type != JTokenType.Boolean
            || envelope["frame"]?.Type != JTokenType.Integer
            || envelope["playbackFrame"]?.Type != JTokenType.Integer
            || envelope["structureVersion"]?.Type != JTokenType.Integer)
        {
            throw new DebugProtocolException("Response requires boolean ok and integer frame, playbackFrame and structureVersion.");
        }
        try
        {
            _ = envelope["frame"]!.Value<long>();
            _ = envelope["playbackFrame"]!.Value<long>();
            _ = envelope["structureVersion"]!.Value<int>();
        }
        catch (Exception e) when (e is OverflowException or InvalidCastException)
        {
            throw new DebugProtocolException("Response counters exceed the supported wire ranges.");
        }
        if (envelope["ok"]!.Value<bool>())
        {
            if (envelope["result"] is not JObject || envelope.ContainsKey("error"))
            {
                throw new DebugProtocolException("Success response requires an object result and no error.");
            }
        }
        else if (envelope["error"] is not JObject error || error["code"]?.Type != JTokenType.String
                                                        || error["detail"]?.Type != JTokenType.String
                                                        || envelope.ContainsKey("result"))
        {
            throw new DebugProtocolException("Failure response requires string error.code/detail and no result.");
        }
    }

    // --- convenience wrappers ----------------------------------------------

    public Response Ping() => Call("ping");
    public void PumpFrames(int count) => Call("pumpFrames", new { count }).Require("pumpFrames");

    /// <summary>Created projects get a "&lt;name&gt; (&lt;namespace&gt;)" display name - tries both forms.</summary>
    public Response OpenProject(string name)
    {
        var response = Call("openProject", new { name });
        if (!response.Ok && name.IndexOf('(') < 0)
        {
            var alternate = Call("openProject", new { name = $"{name} (pixtur.{name})" });
            if (alternate.Ok)
                return alternate;
        }

        return response;
    }

    public Guid AddOp(string symbolName, float posX = 0, float posY = 0)
    {
        var result = Call("addOp", new { symbolName, posX, posY }).Require("addOp");
        return Guid.Parse(result["childId"]!.Value<string>()!);
    }

    public void ConnectOps(Guid sourceChildId, string sourceOutput, Guid targetChildId, string targetInput)
        => Call("connect", new { sourceChildId, sourceOutput, targetChildId, targetInput }).Require("connect");

    public void DeleteOp(Guid childId) => Call("deleteOp", new { childId }).Require("deleteOp");
    public void Pin(Guid childId) => Call("pin", new { childId }).Require("pin");
    public void Select(Guid childId) => Call("select", new { childId }).Require("select");

    public void SetInput(Guid childId, string inputName, JToken value)
        => Call("setInput", new JObject { ["childId"] = childId, ["inputName"] = inputName, ["value"] = value }).Require("setInput");

    /// <summary>Vector4 inputs travel as an X/Y/Z/W object.</summary>
    public void SetVector4Input(Guid childId, string inputName, float x, float y, float z, float w)
        => SetInput(childId, inputName, new JObject { ["X"] = x, ["Y"] = y, ["Z"] = z, ["W"] = w });

    /// <summary>List inputs travel wrapped in a "Values" object.</summary>
    public void SetIntListInput(Guid childId, string inputName, IEnumerable<int> values)
        => SetInput(childId, inputName, new JObject { ["Values"] = new JArray(values) });

    public string? GetOutputValue(Guid? childId = null)
    {
        var result = (childId == null ? Call("getOutput") : Call("getOutput", new { childId })).Require("getOutput");
        return result["value"]?.Type == JTokenType.Null ? null : result["value"]?.Value<string>();
    }

    public JObject GetGraphState(bool includeDefaults = false)
        => Call("getGraphState", new { includeDefaults }).Require("getGraphState");

    /// <param name="target">"output" (the output window's texture) or "ui" (the whole editor window as rendered).</param>
    public void Screenshot(string path, string target = "output") => Call("screenshot", new { path, target }).Require("screenshot");
    public Response Reload(string project) => Call("reload", new { project });
    public void Undo() => Call("undo").Require("undo");

    public JArray GetLogTail(long sinceSeq = -1, string minLevel = "debug", int maxCount = 200)
        => (JArray)Call("getLogTail", new { sinceSeq, minLevel, maxCount }).Require("getLogTail")["entries"]!;

    public long GetLatestLogSeq()
        => Call("getLogTail", new { maxCount = 1 }).Require("getLogTail")["latestSeq"]!.Value<long>();

    public JObject GetMetrics() => Call("getMetrics").Require("getMetrics");

    /// <summary>Fire-and-forget: the editor tears down before it can reply.</summary>
    public void Shutdown()
    {
        if (_disposed)
        {
            return;
        }
        try
        {
            _writer.WriteLine("""{"id":"shutdown","method":"shutdown"}""");
        }
        catch (IOException)
        {
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _tcpClient.Dispose();
        _reader.Dispose();
        _writer.Dispose();
    }

    private readonly TcpClient _tcpClient;
    private readonly StreamReader _reader;
    private readonly StreamWriter _writer;
    private readonly bool _strictResponses;
    private bool _disposed;
    private int _requestCounter;
}

public sealed record Response(bool Ok, JObject? Result, string? ErrorCode, string? ErrorDetail, long Frame, int StructureVersion)
{
    public JObject? Envelope { get; init; }
    public JToken? Id => Envelope?["id"];
    public long? PlaybackFrame => Envelope?["playbackFrame"]?.Value<long>();

    /// <summary>Returns the result payload or throws with the protocol error.</summary>
    public JObject Require(string what)
    {
        if (!Ok)
            throw new DebugProtocolException($"{what} failed: {ErrorCode} - {ErrorDetail}");

        return Result ?? new JObject();
    }
}

public sealed class DebugProtocolException(string message) : Exception(message);
