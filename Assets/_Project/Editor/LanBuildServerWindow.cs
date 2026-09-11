using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>Builds WebGL and serves it over the local network via a Python HTTP server, so it can be opened from a phone/tablet on the same Wi-Fi.</summary>
public sealed class LanBuildServerWindow : EditorWindow
{
    const string BuildPath = "build/WebGl/";
    const string PythonCommandPrefKey = "RiftRaiders.LanBuildServer.PythonCommand";
    const string PortPrefKey = "RiftRaiders.LanBuildServer.Port";
    const string ServerPidPrefKey = "RiftRaiders.LanBuildServer.Pid";
    const string ServerScriptRelativePath = "Library/RiftRaidersLanServer/serve_webgl.py";

    // Unity's WebGL build ships .br (and sometimes .gz) compressed files, but Python's plain
    // `http.server` doesn't send the Content-Encoding header those need — the browser then tries to
    // parse the raw compressed bytes as JS/wasm and fails. This handler adds that header and still
    // guesses the underlying content type by stripping the compression suffix first.
    const string ServerScriptSource = @"import functools
import http.server


class BrotliAwareHandler(http.server.SimpleHTTPRequestHandler):
    extensions_map = dict(http.server.SimpleHTTPRequestHandler.extensions_map)
    extensions_map['.wasm'] = 'application/wasm'

    def end_headers(self):
        path = self.path.split('?', 1)[0]
        if path.endswith('.br'):
            self.send_header('Content-Encoding', 'br')
        elif path.endswith('.gz'):
            self.send_header('Content-Encoding', 'gzip')
        # Builds change on every rebuild during LAN testing; without this the browser can keep
        # reusing a stale (or half-received, from an earlier broken run) cached copy of a file.
        self.send_header('Cache-Control', 'no-store, must-revalidate')
        super().end_headers()

    def guess_type(self, path):
        stripped = path
        if stripped.endswith('.br') or stripped.endswith('.gz'):
            stripped = stripped.rsplit('.', 1)[0]
        return super().guess_type(stripped)


def main():
    import argparse
    parser = argparse.ArgumentParser()
    parser.add_argument('port', type=int, nargs='?', default=8000)
    parser.add_argument('--bind', default='0.0.0.0')
    parser.add_argument('--directory', default='.')
    args = parser.parse_args()

    handler_class = functools.partial(BrotliAwareHandler, directory=args.directory)
    http.server.test(HandlerClass=handler_class, port=args.port, bind=args.bind)


if __name__ == '__main__':
    main()
";

    int _port = 8000;
    string _pythonCommand = "python3";
    Process _serverProcess;
    string _statusMessage;

    [MenuItem("Tools/RiftRaiders/Build/LAN Build Server")]
    public static void Open()
    {
        var window = GetWindow<LanBuildServerWindow>("LAN Build Server");
        window.minSize = new Vector2(380, 220);
    }

    void OnEnable()
    {
        _port = EditorPrefs.GetInt(PortPrefKey, 8000);
        _pythonCommand = EditorPrefs.GetString(PythonCommandPrefKey, "python3");
        TryReattachToRunningServer();
    }

    // A domain reload (e.g. from recompiling scripts) destroys this window's managed state but not
    // the child python process it started, so recover the reference from the PID we persisted.
    void TryReattachToRunningServer()
    {
        var savedPid = EditorPrefs.GetInt(ServerPidPrefKey, -1);
        if (savedPid <= 0)
            return;

        try
        {
            var proc = Process.GetProcessById(savedPid);
            if (!proc.HasExited && IsHttpServerProcess(savedPid))
                _serverProcess = proc;
            else
                EditorPrefs.DeleteKey(ServerPidPrefKey);
        }
        catch (ArgumentException)
        {
            EditorPrefs.DeleteKey(ServerPidPrefKey);
        }
    }

    void OnDisable()
    {
        EditorPrefs.SetInt(PortPrefKey, _port);
        EditorPrefs.SetString(PythonCommandPrefKey, _pythonCommand);
    }

    void OnDestroy()
    {
        StopServer();
    }

    void OnGUI()
    {
        EditorGUILayout.LabelField("Build & Serve WebGL on Local Network", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        _pythonCommand = EditorGUILayout.TextField("Python command", _pythonCommand);
        _port = EditorGUILayout.IntField("Port", _port);

        EditorGUILayout.Space();

        using (new EditorGUI.DisabledScope(IsServerRunning))
        {
            if (GUILayout.Button("Build WebGL", GUILayout.Height(28)))
                BuildWebGl();

            if (GUILayout.Button("Build && Serve", GUILayout.Height(32)))
            {
                if (BuildWebGl())
                    StartServer();
            }
        }

        EditorGUILayout.Space();

        if (IsServerRunning)
        {
            var lanIp = GetLocalIPv4Addresses().FirstOrDefault();
            var url = $"http://{lanIp ?? "?"}:{_port}";

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.SelectableLabel(url, EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight));
                if (GUILayout.Button("Copy", GUILayout.Width(50)))
                    EditorGUIUtility.systemCopyBuffer = url;
            }

            EditorGUILayout.HelpBox(lanIp != null
                    ? "Open this on your phone/tablet over the same Wi-Fi/LAN."
                    : "No local network IP was found.",
                MessageType.Info);

            if (GUILayout.Button("Stop Server", GUILayout.Height(24)))
                StopServer();
        }
        else if (GUILayout.Button("Start Server (existing build)", GUILayout.Height(24)))
        {
            StartServer();
        }

        if (!string.IsNullOrEmpty(_statusMessage))
            EditorGUILayout.HelpBox(_statusMessage, MessageType.None);
    }

    bool IsServerRunning => _serverProcess != null && !_serverProcess.HasExited;

    bool BuildWebGl()
    {
        try
        {
            GameBuilder.PerformWebBuild();
            _statusMessage = "Build finished. Check console for result.";
            return true;
        }
        catch (Exception e)
        {
            _statusMessage = $"Build failed: {e.Message}";
            Debug.LogException(e);
            return false;
        }
    }

    void StartServer()
    {
        var fullPath = Path.GetFullPath(BuildPath);
        if (!Directory.Exists(fullPath) || !File.Exists(Path.Combine(fullPath, "index.html")))
        {
            _statusMessage = $"No WebGL build found at {fullPath}. Build first.";
            return;
        }

        StopServer();
        KillStaleServersOnPort(_port);

        var scriptPath = Path.GetFullPath(ServerScriptRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(scriptPath));
        File.WriteAllText(scriptPath, ServerScriptSource);

        // Deliberately not redirecting stdio: a redirected pipe is read by a managed thread that dies
        // on the next domain reload, leaving the child writing into a dead pipe and hanging mid-response.
        // Letting it inherit the Editor's own stdio survives reloads.
        var psi = new ProcessStartInfo
        {
            FileName = _pythonCommand,
            Arguments = $"\"{scriptPath}\" {_port} --bind 0.0.0.0 --directory \"{fullPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        try
        {
            _serverProcess = Process.Start(psi);
            EditorPrefs.SetInt(ServerPidPrefKey, _serverProcess.Id);
            _statusMessage = "Server started.";
            Application.OpenURL($"http://localhost:{_port}");
        }
        catch (Exception e)
        {
            _statusMessage = $"Failed to start server ('{_pythonCommand}'): {e.Message}";
            Debug.LogException(e);
            _serverProcess = null;
        }
    }

    // Editor script recompiles can orphan a previously started server (see TryReattachToRunningServer);
    // if that reference was lost entirely (e.g. Editor was closed and reopened), find it by port instead.
    static void KillStaleServersOnPort(int port)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "/usr/sbin/lsof",
                Arguments = $"-ti tcp:{port}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            };
            using var lsof = Process.Start(psi);
            var output = lsof.StandardOutput.ReadToEnd();
            lsof.WaitForExit(2000);

            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!int.TryParse(line.Trim(), out var pid) || !IsHttpServerProcess(pid))
                    continue;

                try
                {
                    Process.GetProcessById(pid).Kill();
                    Debug.Log($"[LAN Server] Killed stale http.server (pid {pid}) still bound to port {port}.");
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[LAN Server] Could not kill stale process {pid}: {e.Message}");
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[LAN Server] Could not check for stale servers on port " + port + ": " + e.Message);
        }
    }

    static bool IsHttpServerProcess(int pid)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "/bin/ps",
                Arguments = $"-p {pid} -o command=",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            };
            using var ps = Process.Start(psi);
            var output = ps.StandardOutput.ReadToEnd();
            ps.WaitForExit(1000);
            return output.Contains("RiftRaidersLanServer") || output.Contains("http.server");
        }
        catch
        {
            return false;
        }
    }

    void StopServer()
    {
        if (_serverProcess == null)
            return;

        try
        {
            if (!_serverProcess.HasExited)
            {
                _serverProcess.Kill();
                _serverProcess.WaitForExit(2000);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[LAN Server] Failed to stop cleanly: " + e.Message);
        }
        finally
        {
            _serverProcess.Dispose();
            _serverProcess = null;
            EditorPrefs.DeleteKey(ServerPidPrefKey);
        }
    }


    // Reads the IP straight from the OS's network interfaces instead of resolving the machine's
    // hostname (Dns.GetHostAddresses can return nothing, a stale mDNS entry, or be blocked by macOS's
    // Local Network permission — none of which reflect the interface actually reachable on the LAN).
    static List<string> GetLocalIPv4Addresses()
    {
        var result = new List<string>();
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up)
                continue;
            if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                continue;

            foreach (var addrInfo in ni.GetIPProperties().UnicastAddresses)
            {
                var addr = addrInfo.Address;
                if (addr.AddressFamily != AddressFamily.InterNetwork)
                    continue;
                if (IPAddress.IsLoopback(addr) || addr.ToString().StartsWith("169.254."))
                    continue;
                result.Add(addr.ToString());
            }
        }
        return result;
    }
}
