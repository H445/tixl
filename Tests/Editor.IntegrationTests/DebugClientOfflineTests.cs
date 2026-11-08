using System.Net;
using System.Net.Sockets;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TiXL.DebugClient;
using Xunit;

namespace Editor.IntegrationTests;

[Trait("Category", "Offline")]
public sealed class DebugClientOfflineTests
{
    private static JObject Success(JObject request) => new()
    {
        ["id"] = request["id"]!.DeepClone(), ["ok"] = true, ["result"] = new JObject(),
        ["frame"] = 12, ["playbackFrame"] = 7, ["structureVersion"] = 3,
    };

    [Fact]
    public async Task StrictResponse_PreservesEnvelopeAndDeferredReply()
    {
        using var server = new FakeBridge(async (request, writer) =>
        {
            await Task.Delay(40);
            var reply = Success(request);
            reply["futureField"] = "preserved";
            await writer.WriteLineAsync(reply.ToString(Formatting.None));
        });
        using var client = server.Connect();
        var response = client.Ping();
        Assert.True(response.Ok);
        Assert.Equal("1", response.Id!.Value<string>());
        Assert.Equal(7, response.PlaybackFrame);
        Assert.Equal("preserved", response.Envelope!["futureField"]!.Value<string>());
        Assert.Empty(response.Require("ping"));
        await server.Completion;
        Assert.Equal(1, server.RequestCount);
    }

    [Fact]
    public async Task StrictFailure_PreservesErrorForConvenienceMethods()
    {
        using var server = new FakeBridge((request, writer) =>
        {
            var reply = Success(request);
            reply.Remove("result");
            reply["ok"] = false;
            reply["error"] = new JObject { ["code"] = "NOT_FOUND", ["detail"] = "Missing input" };
            return writer.WriteLineAsync(reply.ToString(Formatting.None));
        });
        using var client = server.Connect();
        var response = client.Call("setInput");
        Assert.False(response.Ok);
        Assert.Equal("NOT_FOUND", response.ErrorCode);
        Assert.Equal("Missing input", response.ErrorDetail);
        Assert.Throws<DebugProtocolException>(() => response.Require("setInput"));
        await server.Completion;
    }

    [Theory]
    [InlineData("wrongId")]
    [InlineData("numericId")]
    [InlineData("missingFrame")]
    [InlineData("stringOk")]
    [InlineData("missingPlayback")]
    [InlineData("missingStructure")]
    [InlineData("missingResult")]
    [InlineData("mixedResultError")]
    [InlineData("badError")]
    [InlineData("overflow")]
    public async Task InvalidEnvelope_FailsAndPreventsStreamReuse(string defect)
    {
        using var server = new FakeBridge((request, writer) =>
        {
            var reply = Success(request);
            switch (defect)
            {
                case "wrongId": reply["id"] = "other"; break;
                case "numericId": reply["id"] = 1; break;
                case "missingFrame": reply.Remove("frame"); break;
                case "stringOk": reply["ok"] = "true"; break;
                case "missingPlayback": reply.Remove("playbackFrame"); break;
                case "missingStructure": reply.Remove("structureVersion"); break;
                case "missingResult": reply.Remove("result"); break;
                case "mixedResultError": reply["error"] = new JObject(); break;
                case "badError": reply["ok"] = false; reply.Remove("result"); reply["error"] = "failure"; break;
                case "overflow": reply["structureVersion"] = long.MaxValue; break;
            }
            return writer.WriteLineAsync(reply.ToString(Formatting.None));
        });
        using var client = server.Connect();
        Assert.Throws<DebugProtocolException>(() => client.Ping());
        Assert.Throws<ObjectDisposedException>(() => client.Ping());
        await server.Completion;
        Assert.Equal(1, server.RequestCount);
    }

    [Fact]
    public async Task InvalidJson_IsVisibleAndClosesStrictSession()
    {
        using var server = new FakeBridge((_, writer) => writer.WriteLineAsync("not JSON"));
        using var client = server.Connect();
        Assert.Throws<JsonReaderException>(() => client.CallRaw("ping"));
        Assert.Throws<ObjectDisposedException>(() => client.Ping());
        await server.Completion;
    }

    [Fact]
    public async Task Disconnect_IsVisibleAndNeverReplayed()
    {
        using var server = new FakeBridge((_, _) => Task.CompletedTask);
        using var client = server.Connect();
        Assert.Throws<IOException>(() => client.Ping());
        Assert.Throws<ObjectDisposedException>(() => client.Ping());
        await server.Completion;
        Assert.Equal(1, server.RequestCount);
    }

    [Fact]
    public async Task Timeout_ClosesSessionWithoutReplayingRequest()
    {
        using var release = new CancellationTokenSource();
        using var server = new FakeBridge(async (_, _) =>
        {
            try
            {
                await Task.Delay(5000, release.Token);
            }
            catch (OperationCanceledException) { }
        });
        using var client = server.Connect(TimeSpan.FromMilliseconds(100));
        try
        {
            Assert.Throws<IOException>(() => client.Call("setInput", new { value = 3 }));
            Assert.Throws<ObjectDisposedException>(() => client.Ping());
        }
        finally
        {
            release.Cancel();
        }
        await server.Completion;
        Assert.Equal(1, server.RequestCount);
    }

    [Fact]
    public async Task SequentialCalls_UseDistinctIdsAndPreserveArguments()
    {
        var ids = new List<string>();
        using var server = new FakeBridge((request, writer) =>
        {
            ids.Add(request["id"]!.Value<string>()!);
            Assert.Equal("setInput", request["method"]!.Value<string>());
            Assert.Equal(JTokenType.Null, request["value"]!.Type);
            Assert.False(request.ContainsKey("update"));
            return writer.WriteLineAsync(Success(request).ToString(Formatting.None));
        }, requestLimit: 2);
        using var client = server.Connect();
        client.CallRaw("setInput", new JObject { ["value"] = JValue.CreateNull() });
        client.CallRaw("setInput", new JObject { ["value"] = JValue.CreateNull() });
        await server.Completion;
        Assert.Equal(["1", "2"], ids);
        Assert.Equal(2, server.RequestCount);
    }

    [Fact]
    public async Task LegacyMode_KeepsPermissiveParsing()
    {
        using var server = new FakeBridge((_, writer) => writer.WriteLineAsync("""{"ok":true,"result":{}}"""));
        using var client = server.Connect(strict: false);
        Assert.True(client.Ping().Ok);
        await server.Completion;
    }

    private sealed class FakeBridge : IDisposable
    {
        internal FakeBridge(Func<JObject, StreamWriter, Task> reply, int requestLimit = 1)
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Completion = Task.Run(async () =>
            {
                using var connection = await _listener.AcceptTcpClientAsync(_cancel.Token);
                using var reader = new StreamReader(connection.GetStream(), Encoding.UTF8);
                using var writer = new StreamWriter(connection.GetStream(), new UTF8Encoding(false)) { AutoFlush = true };
                for (var i = 0; i < requestLimit; i++)
                {
                    var line = await reader.ReadLineAsync(_cancel.Token);
                    Assert.NotNull(line);
                    RequestCount++;
                    await reply(JObject.Parse(line), writer);
                }
            });
        }

        internal DebugProtocolClient Connect(TimeSpan? timeout = null, bool strict = true)
        {
            Assert.True(DebugProtocolClient.TryConnect(((IPEndPoint)_listener.LocalEndpoint).Port,
                                                       TimeSpan.FromSeconds(2), out var client, strict,
                                                       timeout ?? TimeSpan.FromSeconds(3)));
            return client!;
        }

        internal Task Completion { get; }
        internal int RequestCount { get; private set; }

        public void Dispose()
        {
            _cancel.Cancel();
            _listener.Stop();
            _cancel.Dispose();
        }

        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cancel = new(TimeSpan.FromSeconds(10));
    }
}
