using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace GlitterVis;

/// <summary>
/// A tiny control port on 127.0.0.1:8795 (this PC only) so MoBetter can steer GlitterVis.
/// GET /status returns JSON; GET /cmd/&lt;name&gt; queues a command (next, prev, lock, calm, title, quit, show,
/// fullscreen, window, size/small|medium|large, scene/&lt;name&gt;). Commands run on the render thread.
/// The same port makes GlitterVis single-instance: a second copy sends "show" and exits.
/// </summary>
public sealed class Control : IDisposable
{
    public const int Port = 8795;
    public readonly ConcurrentQueue<string> Commands = new();
    public Func<object> Status = () => new { };
    TcpListener listener;
    volatile bool running;

    public bool Start()
    {
        try { listener = new TcpListener(IPAddress.Loopback, Port); listener.Start(); }
        catch (SocketException) { return false; }
        running = true;
        new Thread(Loop) { IsBackground = true, Name = "control" }.Start();
        return true;
    }

    void Loop()
    {
        while (running)
        {
            try
            {
                using var c = listener.AcceptTcpClient();
                c.ReceiveTimeout = 2000; c.SendTimeout = 2000;
                var s = c.GetStream();
                var buf = new byte[2048];
                int n = s.Read(buf, 0, buf.Length);
                string first = Encoding.ASCII.GetString(buf, 0, n).Split('\n')[0];
                var parts = first.Split(' ');
                string path = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : "/";
                string body; int code = 200;
                if (path == "/status") body = JsonSerializer.Serialize(Status());
                else if (path.StartsWith("/cmd/") && path.Length < 60) { Commands.Enqueue(path[5..]); body = "{\"ok\":true}"; }
                else { code = 404; body = "{\"error\":\"not found\"}"; }
                var bytes = Encoding.UTF8.GetBytes(body);
                var head = $"HTTP/1.1 {code} OK\r\nContent-Type: application/json\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n";
                s.Write(Encoding.ASCII.GetBytes(head)); s.Write(bytes);
            }
            catch (Exception ex) when (running) { Log.Write("control: " + ex.Message); }
            catch { }
        }
    }

    /// <summary>Send a command to an already-running copy. True if one answered.</summary>
    public static bool Send(string cmd)
    {
        try
        {
            using var c = new TcpClient();
            c.Connect(IPAddress.Loopback, Port);
            var s = c.GetStream();
            s.Write(Encoding.ASCII.GetBytes($"GET /cmd/{cmd} HTTP/1.1\r\nHost: localhost\r\n\r\n"));
            s.ReadByte();
            return true;
        }
        catch { return false; }
    }

    public void Dispose() { running = false; try { listener?.Stop(); } catch { } }
}

/// <summary>Remembered between runs: full screen or window, and the window size.</summary>
public sealed class Settings
{
    public bool Fullscreen { get; set; } = true;
    public string Size { get; set; } = "medium";
    static string FilePath => Path.Combine(AppContext.BaseDirectory, "glittervis-settings.json");

    public static Settings Load()
    {
        try { return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new(); } catch { return new(); }
    }

    public void Save()
    {
        try { File.WriteAllText(FilePath, JsonSerializer.Serialize(this)); } catch (Exception e) { Log.Write("settings not saved: " + e.Message); }
    }
}
