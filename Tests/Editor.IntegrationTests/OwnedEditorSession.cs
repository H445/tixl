using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using Newtonsoft.Json.Linq;
using TiXL.DebugClient;

namespace Editor.IntegrationTests;

internal sealed class OwnedEditorSession : IDisposable
{
    private OwnedEditorSession(OwnedEditorPaths paths, string evidenceDirectory)
    {
        Paths = paths;
        EvidenceDirectory = evidenceDirectory;
    }

    internal DebugProtocolClient Client { get; private set; } = null!;
    internal OwnedEditorPaths Paths { get; }
    internal string EvidenceDirectory { get; }
    internal Guid HomeSymbolId { get; private set; }
    internal string ProjectDirectory { get; private set; } = "";

    internal static OwnedEditorSession Start(string executable, string backend, bool createProject = true)
    {
        executable = Path.GetFullPath(executable);
        if (!File.Exists(executable) || backend is not ("d3d11" or "vulkan")
                                     || (!OperatingSystem.IsWindows() && backend == "d3d11"))
        {
            throw new ArgumentException("Select an existing editor executable and supported explicit backend.");
        }
        var binaryDirectory = Path.GetDirectoryName(executable)!;
        if (OperatingSystem.IsWindows()
            && !string.Equals(Environment.GetEnvironmentVariable("T3_ASSEMBLY_PATH", EnvironmentVariableTarget.User),
                              binaryDirectory, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("This editor would change the shared T3_ASSEMBLY_PATH setting. Use a dedicated test account already configured for this editor.");
        }
        var version = AssemblyName.GetAssemblyName(Path.Combine(binaryDirectory, "Core.dll")).Version
                      ?? throw new InvalidDataException("Cannot identify editor Core version.");
        var home = UserFolder(Environment.SpecialFolder.UserProfile);
        var cache = OperatingSystem.IsWindows() ? UserFolder(Environment.SpecialFolder.LocalApplicationData)
                    : OperatingSystem.IsMacOS() ? Path.Combine(home, "Library", "Caches")
                    : Environment.GetEnvironmentVariable("XDG_CACHE_HOME") is { } xdg && Path.IsPathFullyQualified(xdg)
                        ? xdg : Path.Combine(home, ".cache");
        var paths = new OwnedEditorPaths($"{version.Major}.{version.Minor}", Guid.NewGuid().ToString("N"),
                                         UserFolder(Environment.SpecialFolder.ApplicationData), cache,
                                         UserFolder(Environment.SpecialFolder.MyDocuments), Path.GetTempPath());
        var evidence = Path.Combine(Path.GetTempPath(), "tixl-contract-evidence", paths.RunId);
        Directory.CreateDirectory(evidence);
        var session = new OwnedEditorSession(paths, evidence);
        try
        {
            paths.Claim();
            // A released port can be raced; the unique getPaths handshake is the ownership check.
            var port = FindFreePort();
            var start = EditorFixture.CreateStartInfo(executable, port);
            start.CreateNoWindow = true;
            start.WindowStyle = ProcessWindowStyle.Hidden;
            start.ArgumentList.Add("--override-version-id=" + paths.ProfileId);
            start.Environment["TIXL_OVERRIDE_VERSION_ID"] = paths.ProfileId;
            start.Environment["TIXL_BACKEND"] = backend;
            session._process = Process.Start(start) ?? throw new IOException("Editor did not start.");

            var timer = Stopwatch.StartNew();
            DebugProtocolClient? client = null;
            while (timer.Elapsed < TimeSpan.FromSeconds(120))
            {
                if (session._process.HasExited)
                {
                    throw new IOException($"Owned editor exited with code {session._process.ExitCode} before connecting. Evidence: {evidence}");
                }
                if (DebugProtocolClient.TryConnect(port, TimeSpan.FromSeconds(1), out client, strictResponses: true,
                                                  responseTimeout: TimeSpan.FromSeconds(60)))
                {
                    break;
                }
            }
            if (client == null)
            {
                throw new TimeoutException("Owned editor did not open its bridge. Evidence: " + evidence);
            }
            session.Client = client;
            paths.Verify(client.Call("getPaths").Require("getPaths"));
            if (session._process.HasExited)
            {
                throw new IOException("Owned editor exited during its ownership handshake.");
            }
            session._verified = true;
            var editorVersion = client.Call("getVersion").Require("getVersion");
            File.WriteAllText(Path.Combine(evidence, "session.json"), new JObject
            {
                ["executable"] = executable, ["backend"] = backend, ["processId"] = session._process.Id,
                ["profileId"] = paths.ProfileId, ["reportedVersion"] = editorVersion,
                ["paths"] = client.Call("getPaths").Require("getPaths"),
                ["buildMatch"] = "Unverified: assembly version does not prove source identity",
            }.ToString());

            if (!createProject)
            {
                return session;
            }
            var projectName = "BridgeContract" + paths.RunId;
            var created = client.Call("newProject", new { name = "contract." + projectName }).Require("newProject");
            session.HomeSymbolId = Guid.Parse(created["homeSymbolId"]!.Value<string>()!);
            session.ProjectDirectory = Path.Combine(paths["projects"], projectName);
            if (!Directory.Exists(session.ProjectDirectory))
            {
                throw new IOException("Project was not created in the owned project root.");
            }
            session.OpenProject();
            return session;
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(evidence, "failure.txt"), error.ToString());
            session.Stop();
            session.CopyEvidence();
            // Failed startup paths are retained for diagnosis; never guess cleanup paths from a foreign response.
            throw;
        }
    }

    internal void OpenProject()
    {
        Client.Call("openProject", new { symbolId = HomeSymbolId }).Require("openProject");
        Client.PumpFrames(3);
    }

    internal bool WaitForExit(TimeSpan timeout) => _process?.WaitForExit((int)timeout.TotalMilliseconds) == true;

    private static int FindFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string UserFolder(Environment.SpecialFolder folder)
    {
        var path = Environment.GetFolderPath(folder, Environment.SpecialFolderOption.DoNotVerify);
        if (!Path.IsPathFullyQualified(path))
        {
            throw new InvalidOperationException($"Cannot establish an isolated {folder} path on this runner.");
        }
        return path;
    }

    private void Stop()
    {
        if (_process != null)
        {
            if (!_process.HasExited)
            {
                if (_verified)
                {
                    Client.Shutdown();
                }
                if (!_process.WaitForExit(_verified ? 10000 : 100))
                {
                    _process.Kill(entireProcessTree: true);
                    if (!_process.WaitForExit(10000))
                    {
                        throw new IOException("Owned editor has not exited; keeping its paths intact.");
                    }
                }
            }
            _process.Dispose();
            _process = null;
        }
        Client?.Dispose();
    }

    private void CopyEvidence()
    {
        var logs = Path.Combine(Paths["settings"], "Log");
        if (Directory.Exists(logs))
        {
            foreach (var file in Directory.EnumerateFiles(logs))
            {
                File.Copy(file, Path.Combine(EvidenceDirectory, Path.GetFileName(file)), overwrite: true);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        Stop();
        CopyEvidence();
        Paths.Delete();
        File.WriteAllText(Path.Combine(EvidenceDirectory, "cleanup.txt"), "Owned process exited; verified session roots removed.");
    }

    private Process? _process;
    private bool _verified;
    private bool _disposed;
}
