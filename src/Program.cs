using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace GlitterVis;

/// <summary>
/// GlitterVis: a full-screen music visualizer for the TV. It listens to whatever the PC plays (Winamp, VLC, anything),
/// shows one of several GPU scenes, crossfades to the next on a beat every ~40 seconds or on a new track, colors the
/// scenes from the album cover, and shows a title card when a track starts.
/// Keys: N or Right = next scene, Left = previous, L = lock this scene, C = calm mode, T = show the title, Esc or Q = quit.
/// Options: --window (a window instead of full screen), --height 720|1080 (render size, default 1080),
///          --snapshot <folder> (render every scene to PNG files and exit; for checking without a screen).
/// </summary>
public sealed class Vis : GameWindow
{
    static readonly string[] SceneNames = { "nebula", "liquid", "aurora", "tunnel", "galaxy", "horizon", "kaleido" };
    static readonly Dictionary<string, float[][]> DefaultPalettes = new()
    {
        ["nebula"] = new[] { new[] { 0.05f, 0.02f, 0.12f }, new[] { 0.35f, 0.08f, 0.45f }, new[] { 0.9f, 0.35f, 0.5f }, new[] { 0.4f, 0.75f, 1.0f } },
        ["liquid"] = new[] { new[] { 0.1f, 0.0f, 0.2f }, new[] { 0.9f, 0.2f, 0.4f }, new[] { 1.0f, 0.7f, 0.2f }, new[] { 0.2f, 0.8f, 0.9f } },
        ["aurora"] = new[] { new[] { 0.02f, 0.1f, 0.15f }, new[] { 0.1f, 0.9f, 0.5f }, new[] { 0.3f, 0.5f, 1.0f }, new[] { 0.8f, 0.3f, 0.9f } },
        ["tunnel"] = new[] { new[] { 0.05f, 0.05f, 0.2f }, new[] { 0.2f, 0.6f, 1.0f }, new[] { 1.0f, 0.3f, 0.6f }, new[] { 1.0f, 0.9f, 0.5f } },
        ["galaxy"] = new[] { new[] { 0.1f, 0.05f, 0.2f }, new[] { 0.5f, 0.3f, 0.9f }, new[] { 0.9f, 0.6f, 0.4f }, new[] { 1.0f, 0.95f, 0.85f } },
        ["horizon"] = new[] { new[] { 0.15f, 0.05f, 0.3f }, new[] { 0.95f, 0.35f, 0.4f }, new[] { 1.0f, 0.7f, 0.3f }, new[] { 0.4f, 0.9f, 1.0f } },
        ["kaleido"] = new[] { new[] { 0.3f, 0.0f, 0.4f }, new[] { 1.0f, 0.2f, 0.3f }, new[] { 1.0f, 0.8f, 0.1f }, new[] { 0.1f, 0.9f, 0.8f } },
    };

    readonly Audio audio = new();
    readonly int renderH;
    readonly string snapshotDir;
    int[] programs;
    int quadVao, compositeProg, overlayProg, specTex, titleTex;
    int[] fbo = new int[2], fboTex = new int[2];
    int rw, rh;

    int current = 0, next = -1;
    double fadeStart = -1;
    const double FadeSeconds = 3.0, SceneSeconds = 40.0;
    double sceneStart;
    bool locked, calm;
    float calmMix;
    double clock, visTime;
    int frames; double fpsMark;

    float[][] palette = Clone(DefaultPalettes["nebula"]);
    float[][] coverPalette;
    string lastTrackKey = "", coverPath;
    double lastPoll = -10, titleShownAt = -100;
    int titleW = 1, titleH = 1;

    public Vis(GameWindowSettings g, NativeWindowSettings n, int renderHeight, string snapshot) : base(g, n)
    {
        renderH = renderHeight;
        snapshotDir = snapshot;
    }

    static float[][] Clone(float[][] p) => p.Select(c => (float[])c.Clone()).ToArray();

    protected override void OnLoad()
    {
        base.OnLoad();
        string dir = Path.Combine(AppContext.BaseDirectory, "shaders");
        string common = File.ReadAllText(Path.Combine(dir, "common.glsl"));
        programs = SceneNames.Select(n => Build(Vert, common + "\n" + File.ReadAllText(Path.Combine(dir, n + ".frag")), n)).ToArray();
        compositeProg = Build(Vert, Composite, "composite");
        overlayProg = Build(OverlayVert, OverlayFrag, "overlay");
        quadVao = GL.GenVertexArray();

        specTex = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, specTex);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.R32f, Audio.Bands, 1, 0, PixelFormat.Red, PixelType.Float, new float[Audio.Bands]);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        titleTex = GL.GenTexture();

        MakeTargets();
        if (snapshotDir == null) { audio.Start(); CursorState = CursorState.Hidden; }
        current = new Random().Next(SceneNames.Length);
        palette = Clone(DefaultPalettes[SceneNames[current]]);
        Log.Write($"started: render {rw}x{rh}, scene {SceneNames[current]}");
    }

    void MakeTargets()
    {
        int sh = snapshotDir != null ? 540 : Math.Min(renderH, Math.Max(ClientSize.Y, 1));
        rh = sh;
        rw = Math.Max(1, (int)Math.Round(sh * (double)Math.Max(ClientSize.X, 16) / Math.Max(ClientSize.Y, 9)));
        if (snapshotDir != null) rw = 960;
        for (int i = 0; i < 2; i++)
        {
            if (fbo[i] != 0) { GL.DeleteFramebuffer(fbo[i]); GL.DeleteTexture(fboTex[i]); }
            fboTex[i] = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, fboTex[i]);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba16f, rw, rh, 0, PixelFormat.Rgba, PixelType.Float, IntPtr.Zero);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            fbo[i] = GL.GenFramebuffer();
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, fbo[i]);
            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, fboTex[i], 0);
        }
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    protected override void OnResize(ResizeEventArgs e) { base.OnResize(e); if (programs != null && snapshotDir == null) MakeTargets(); }

    protected override void OnUpdateFrame(FrameEventArgs e)
    {
        base.OnUpdateFrame(e);
        var k = KeyboardState;
        if (k.IsKeyPressed(Keys.Escape) || k.IsKeyPressed(Keys.Q)) Close();
        if (k.IsKeyPressed(Keys.N) || k.IsKeyPressed(Keys.Right)) StartFade((current + 1) % SceneNames.Length);
        if (k.IsKeyPressed(Keys.Left)) StartFade((current + SceneNames.Length - 1) % SceneNames.Length);
        if (k.IsKeyPressed(Keys.L)) locked = !locked;
        if (k.IsKeyPressed(Keys.C)) calm = !calm;
        if (k.IsKeyPressed(Keys.T)) titleShownAt = clock;
    }

    void StartFade(int to)
    {
        if (to == current || next >= 0) return;
        next = to; fadeStart = clock;
    }

    protected override void OnRenderFrame(FrameEventArgs e)
    {
        base.OnRenderFrame(e);
        if (snapshotDir != null) { Snapshots(); Close(); return; }
        float dt = (float)Math.Min(e.Time, 0.1);
        clock += dt;
        calmMix += ((calm ? 1f : 0f) - calmMix) * Math.Min(1, dt * 0.8f);
        visTime += dt * (1 - calmMix * 0.6);
        audio.Update(dt, clock);
        frames++;
        if (clock - fpsMark >= 30) { Log.Write($"fps {frames / (clock - fpsMark):F1} at {rw}x{rh}, scene {SceneNames[current]}"); frames = 0; fpsMark = clock; }

        PollTrack();
        AutoSwitch();
        BlendPalette(dt);

        GL.BindTexture(TextureTarget.Texture2D, specTex);
        GL.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, Audio.Bands, 1, PixelFormat.Red, PixelType.Float, audio.Spectrum);

        DrawScene(current, fbo[0]);
        float fade = 0;
        if (next >= 0)
        {
            DrawScene(next, fbo[1]);
            fade = (float)Math.Clamp((clock - fadeStart) / FadeSeconds, 0, 1);
            fade = fade * fade * (3 - 2 * fade);
            if (clock - fadeStart >= FadeSeconds) { current = next; next = -1; sceneStart = clock; fade = 0; (fbo[0], fbo[1]) = (fbo[1], fbo[0]); (fboTex[0], fboTex[1]) = (fboTex[1], fboTex[0]); }
        }

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        GL.Viewport(0, 0, ClientSize.X, ClientSize.Y);
        GL.UseProgram(compositeProg);
        GL.ActiveTexture(TextureUnit.Texture0); GL.BindTexture(TextureTarget.Texture2D, fboTex[0]);
        GL.ActiveTexture(TextureUnit.Texture1); GL.BindTexture(TextureTarget.Texture2D, fboTex[1]);
        GL.Uniform1(GL.GetUniformLocation(compositeProg, "uA"), 0);
        GL.Uniform1(GL.GetUniformLocation(compositeProg, "uB"), 1);
        GL.Uniform1(GL.GetUniformLocation(compositeProg, "uFade"), next >= 0 ? fade : 0f);
        GL.Uniform2(GL.GetUniformLocation(compositeProg, "iResolution"), new Vector2(ClientSize.X, ClientSize.Y));
        GL.BindVertexArray(quadVao);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);

        DrawTitle();
        SwapBuffers();
    }

    void DrawScene(int i, int target)
    {
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, target);
        GL.Viewport(0, 0, rw, rh);
        int p = programs[i];
        GL.UseProgram(p);
        GL.Uniform2(GL.GetUniformLocation(p, "iResolution"), new Vector2(rw, rh));
        GL.Uniform1(GL.GetUniformLocation(p, "iTime"), (float)(visTime + i * 37.0));
        GL.Uniform1(GL.GetUniformLocation(p, "uBass"), audio.Bass);
        GL.Uniform1(GL.GetUniformLocation(p, "uMid"), audio.Mid);
        GL.Uniform1(GL.GetUniformLocation(p, "uTreble"), audio.Treble);
        GL.Uniform1(GL.GetUniformLocation(p, "uLevel"), audio.Level);
        GL.Uniform1(GL.GetUniformLocation(p, "uBeat"), audio.Beat);
        GL.Uniform1(GL.GetUniformLocation(p, "uCalm"), calmMix);
        GL.ActiveTexture(TextureUnit.Texture0); GL.BindTexture(TextureTarget.Texture2D, specTex);
        GL.Uniform1(GL.GetUniformLocation(p, "uSpec"), 0);
        var flat = palette.SelectMany(c => c).ToArray();
        GL.Uniform3(GL.GetUniformLocation(p, "uPal"), 4, flat);
        GL.BindVertexArray(quadVao);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
    }

    void AutoSwitch()
    {
        if (locked || next >= 0) return;
        double age = clock - sceneStart;
        bool onBeat = clock - audio.LastBeatTime < 0.05;
        if ((age > SceneSeconds && onBeat) || age > SceneSeconds + 8)
            StartFade((current + 1 + new Random().Next(SceneNames.Length - 1)) % SceneNames.Length);
    }

    void BlendPalette(float dt)
    {
        var target = coverPalette ?? DefaultPalettes[SceneNames[next >= 0 ? next : current]];
        float a = Math.Min(1, dt * 0.6f);
        for (int i = 0; i < 4; i++) for (int c = 0; c < 3; c++) palette[i][c] += (target[i][c] - palette[i][c]) * a;
    }

    void PollTrack()
    {
        if (clock - lastPoll < 1.0) return;
        lastPoll = clock;
        var t = TrackInfo.Read();
        if (t == null || string.IsNullOrWhiteSpace(t.Title)) return;
        string key = t.Artist + "|" + t.Title + "|" + t.File;
        if (key == lastTrackKey) return;
        lastTrackKey = key;
        coverPath = TrackInfo.FindCover(t.File);
        coverPalette = coverPath != null ? Art.Palette(coverPath) : null;
        try
        {
            var (rgba, w, h) = Art.TitleCard(t.Artist, t.Title, coverPath);
            titleW = w; titleH = h;
            GL.BindTexture(TextureTarget.Texture2D, titleTex);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, w, h, 0, PixelFormat.Rgba, PixelType.UnsignedByte, rgba);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            titleShownAt = clock;
        }
        catch (Exception ex) { Log.Write("title card failed: " + ex.Message); }
        if (clock > 5 && !locked) StartFade((current + 1 + new Random().Next(SceneNames.Length - 1)) % SceneNames.Length);
        Log.Write($"track: {t.Artist} - {t.Title} (cover: {(coverPath ?? "none")})");
    }

    void DrawTitle()
    {
        double age = clock - titleShownAt;
        if (age < 0 || age > 10) return;
        float alpha = (float)Math.Min(Math.Min(age / 1.2, 1), Math.Min((10 - age) / 1.8, 1));
        alpha = alpha * alpha * (3 - 2 * alpha);
        float scale = ClientSize.Y / 1080f;
        float w = titleW * scale, h = titleH * scale, x = 70 * scale, y = 70 * scale;
        GL.Enable(EnableCap.Blend);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        GL.UseProgram(overlayProg);
        GL.Uniform4(GL.GetUniformLocation(overlayProg, "uRect"), new Vector4(x / ClientSize.X * 2 - 1, y / ClientSize.Y * 2 - 1, w / ClientSize.X * 2, h / ClientSize.Y * 2));
        GL.Uniform1(GL.GetUniformLocation(overlayProg, "uAlpha"), alpha);
        GL.ActiveTexture(TextureUnit.Texture0); GL.BindTexture(TextureTarget.Texture2D, titleTex);
        GL.Uniform1(GL.GetUniformLocation(overlayProg, "uTex"), 0);
        GL.BindVertexArray(quadVao);
        GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);
        GL.Disable(EnableCap.Blend);
    }

    void Snapshots()
    {
        Directory.CreateDirectory(snapshotDir);
        var fake = new float[Audio.Bands];
        for (int b = 0; b < Audio.Bands; b++) fake[b] = 0.25f + 0.55f * MathF.Exp(-b / 14f) + 0.15f * MathF.Sin(b * 0.7f) * 0.5f;
        audio.Bass = 0.8f; audio.Mid = 0.5f; audio.Treble = 0.35f; audio.Level = 0.5f; audio.Beat = 0.6f;
        Array.Copy(fake, audio.Spectrum, fake.Length);
        GL.BindTexture(TextureTarget.Texture2D, specTex);
        GL.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, Audio.Bands, 1, PixelFormat.Red, PixelType.Float, audio.Spectrum);
        for (int i = 0; i < SceneNames.Length; i++)
        {
            palette = Clone(DefaultPalettes[SceneNames[i]]);
            visTime = 12.3;
            DrawScene(i, fbo[0]);
            var px = new byte[rw * rh * 4];
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, fbo[0]);
            GL.ReadPixels(0, 0, rw, rh, PixelFormat.Bgra, PixelType.UnsignedByte, px);
            using var bmp = new System.Drawing.Bitmap(rw, rh, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var d = bmp.LockBits(new System.Drawing.Rectangle(0, 0, rw, rh), System.Drawing.Imaging.ImageLockMode.WriteOnly, bmp.PixelFormat);
            for (int y = 0; y < rh; y++) System.Runtime.InteropServices.Marshal.Copy(px, (rh - 1 - y) * rw * 4, d.Scan0 + y * d.Stride, rw * 4);
            bmp.UnlockBits(d);
            bmp.Save(Path.Combine(snapshotDir, $"{i + 1}-{SceneNames[i]}.png"));
        }
        Log.Write("snapshots written to " + snapshotDir);
    }

    int Build(string vs, string fs, string name)
    {
        int v = Compile(ShaderType.VertexShader, vs, name), f = Compile(ShaderType.FragmentShader, fs, name);
        int p = GL.CreateProgram();
        GL.AttachShader(p, v); GL.AttachShader(p, f); GL.LinkProgram(p);
        GL.GetProgram(p, GetProgramParameterName.LinkStatus, out int ok);
        if (ok == 0) throw new Exception($"link {name}: {GL.GetProgramInfoLog(p)}");
        return p;
    }

    static int Compile(ShaderType t, string src, string name)
    {
        int s = GL.CreateShader(t);
        GL.ShaderSource(s, src); GL.CompileShader(s);
        GL.GetShader(s, ShaderParameter.CompileStatus, out int ok);
        if (ok == 0) throw new Exception($"compile {name} ({t}): {GL.GetShaderInfoLog(s)}");
        return s;
    }

    const string Vert = "#version 330 core\nvoid main(){ vec2 p = vec2((gl_VertexID << 1) & 2, gl_VertexID & 2); gl_Position = vec4(p * 2.0 - 1.0, 0.0, 1.0); }";
    const string Composite = "#version 330 core\nout vec4 o; uniform sampler2D uA, uB; uniform float uFade; uniform vec2 iResolution;\n" +
                             "void main(){ vec2 uv = gl_FragCoord.xy / iResolution; o = vec4(mix(texture(uA, uv).rgb, texture(uB, uv).rgb, uFade), 1.0); }";
    const string OverlayVert = "#version 330 core\nuniform vec4 uRect; out vec2 vUv;\n" +
                               "void main(){ vec2 c = vec2(gl_VertexID & 1, (gl_VertexID >> 1) & 1); vUv = vec2(c.x, 1.0 - c.y); gl_Position = vec4(uRect.xy + c * uRect.zw, 0.0, 1.0); }";
    const string OverlayFrag = "#version 330 core\nin vec2 vUv; out vec4 o; uniform sampler2D uTex; uniform float uAlpha;\n" +
                               "void main(){ vec4 t = texture(uTex, vUv); o = vec4(t.rgb, t.a * uAlpha); }";

    protected override void OnUnload() { audio.Dispose(); base.OnUnload(); }

    [STAThread]
    static void Main(string[] args)
    {
        bool windowed = args.Contains("--window");
        int h = 1080;
        int hi = Array.IndexOf(args, "--height"); if (hi >= 0 && hi + 1 < args.Length) int.TryParse(args[hi + 1], out h);
        string snap = null;
        int si = Array.IndexOf(args, "--snapshot"); if (si >= 0 && si + 1 < args.Length) snap = args[si + 1];
        var gws = new GameWindowSettings { UpdateFrequency = 60 };
        var nws = new NativeWindowSettings
        {
            Title = "GlitterVis",
            APIVersion = new Version(3, 3),
            Profile = ContextProfile.Core,
            ClientSize = snap != null ? new Vector2i(960, 540) : new Vector2i(1280, 720),
            WindowState = snap != null || windowed ? WindowState.Normal : WindowState.Fullscreen,
            StartVisible = snap == null,
        };
        try
        {
            using var w = new Vis(gws, nws, h, snap);
            w.VSync = VSyncMode.On;
            w.Run();
        }
        catch (Exception e) { Log.Write("fatal: " + e); throw; }
    }
}
