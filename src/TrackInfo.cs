using System.Runtime.InteropServices;
using System.Text;

namespace GlitterVis;

/// <summary>
/// What Winamp is playing: "Artist - Title" from Winamp's window title, and the file's folder (through Winamp's own
/// IPC messages) so the cover picture (folder.jpg, cover.jpg, front.jpg, AlbumArt*.jpg) can color the scenes.
/// Read-only: it never changes anything in Winamp. Other players: the visuals still react, there is just no title card.
/// </summary>
public static class TrackInfo
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string cls, string title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder sb, int max);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] static extern bool ReadProcessMemory(IntPtr p, IntPtr addr, byte[] buf, int size, out IntPtr read);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);

    const uint WM_USER = 0x400;
    const int IPC_ISPLAYING = 104, IPC_GETLISTPOS = 125, IPC_GETPLAYLISTFILEW = 214;

    public record Track(string Artist, string Title, string File, bool Playing);

    public static Track Read()
    {
        var h = FindWindow("Winamp v1.x", null);
        if (h == IntPtr.Zero) return null;
        var sb = new StringBuilder(512);
        GetWindowText(h, sb, sb.Capacity);
        string t = sb.ToString();
        // "123. Artist - Title - Winamp [Paused]"
        t = System.Text.RegularExpressions.Regex.Replace(t, @"^\d+\.\s*", "");
        int w = t.LastIndexOf(" - Winamp", StringComparison.Ordinal);
        if (w >= 0) t = t[..w];
        string artist = "", title = t.Trim();
        int dash = title.IndexOf(" - ", StringComparison.Ordinal);
        if (dash > 0) { artist = title[..dash].Trim(); title = title[(dash + 3)..].Trim(); }
        bool playing = SendMessage(h, WM_USER, IntPtr.Zero, (IntPtr)IPC_ISPLAYING) == (IntPtr)1;
        string file = null;
        try
        {
            int pos = (int)SendMessage(h, WM_USER, IntPtr.Zero, (IntPtr)IPC_GETLISTPOS);
            IntPtr addr = SendMessage(h, WM_USER, (IntPtr)pos, (IntPtr)IPC_GETPLAYLISTFILEW);
            if (addr != IntPtr.Zero)
            {
                GetWindowThreadProcessId(h, out uint pid);
                IntPtr p = OpenProcess(0x0010 /*VM_READ*/, false, pid);
                if (p != IntPtr.Zero)
                {
                    var buf = new byte[1040];
                    if (ReadProcessMemory(p, addr, buf, buf.Length, out _))
                    {
                        string s = Encoding.Unicode.GetString(buf);
                        int z = s.IndexOf('\0');
                        file = z >= 0 ? s[..z] : s;
                    }
                    CloseHandle(p);
                }
            }
        }
        catch { }
        return new Track(artist, title, file, playing);
    }

    public static string FindCover(string file)
    {
        try
        {
            if (string.IsNullOrEmpty(file) || file.Contains("://")) return null;
            string dir = Path.GetDirectoryName(file);
            if (dir == null || !Directory.Exists(dir)) return null;
            foreach (var name in new[] { "folder.jpg", "cover.jpg", "front.jpg", "folder.png", "cover.png", "front.png" })
            {
                var f = Path.Combine(dir, name);
                if (File.Exists(f)) return f;
            }
            var any = Directory.GetFiles(dir, "AlbumArt*.jpg").FirstOrDefault()
                      ?? Directory.GetFiles(dir, "*.jpg").OrderByDescending(f => new FileInfo(f).Length).FirstOrDefault();
            return any;
        }
        catch { return null; }
    }
}
