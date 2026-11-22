using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using GTA;
using GTA.Native;
using GTA.UI;

/// <summary>
/// NightShimmer - ban dem lung linh: cac dom sang mo (bokeh) nhieu mau troi nhe va cac tia sao loe len roi tat.
/// Chi hien ve dem, rat mo va nho nen khong lam canh sang len. Troi mua thi dom sang to va ro hon mot chut.
/// Chinh thong so bang NightShimmer/settings.ini (tu nap lai moi 2 giay).
/// </summary>
public class NightShimmer : Script
{
    class Cfg
    {
        public float Strength       = 0.8f;    // do ro chung cua hieu ung (0 = tat, 1.5 = ro)
        public int   BokehCount     = 34;      // so dom sang mo
        public float BokehSize      = 26f;     // ban kinh dom sang mo (px, tinh o man hinh 1080p)
        public int   GlintCount     = 12;      // so tia sao loe cung luc
        public float GlintSize      = 22f;     // ban kinh tia sao (px)
        public float Drift          = 6f;      // toc do troi cua dom sang mo (px/giay)
        public float TwinkleSpeed   = 1.0f;    // toc do nhap nhay (1 = binh thuong)
        public int   Palette        = 0;       // 0 = nhieu mau, 1 = vang am, 2 = xanh lanh, 3 = trang
        public float BandTop        = 0.12f;   // dai man hinh co hieu ung, ti le chieu cao
        public float BandBottom     = 0.82f;
        public bool  OnlyAtNight    = true;    // false = ca ban ngay cung bat
        public bool  ForceNight     = false;   // true = luon la ban dem (de thu khi troi sang)
        public float RainBoost      = 0.5f;    // troi mua: dom sang to va ro hon bay nhieu
        public float FadeSpeed      = 0.3f;    // toc do len/xuong khi troi toi/sang
        public bool  HideInInterior = true;    // vao nha thi tat
    }

    class Bokeh
    {
        public float X, Y, R, Phase, Freq, Alpha, VX, VY;
        public int Cr, Cg, Cb;
    }

    class Glint
    {
        public float X, Y, R, Age, Life, Alpha;
        public int Cr, Cg, Cb;
    }

    readonly List<Bokeh> bokehs = new List<Bokeh>();
    readonly List<Glint> glints = new List<Glint>();
    readonly Random rng = new Random();
    Cfg cfg = new Cfg();
    CustomSprite bokehSprite;
    CustomSprite glintSprite;
    string iniPath;
    DateTime iniStamp = DateTime.MinValue;
    float reloadTimer;
    float level;       // muc hieu ung hien tai 0..1
    float clock;       // dong ho noi bo cho nhap nhay
    bool enabled = true;
    bool loadFailed;

    public NightShimmer()
    {
        Tick += OnTick;
        KeyUp += OnKeyUp;

        string dir = null;
        try { dir = Path.GetDirectoryName(Filename); } catch { }
        string folder = dir != null ? Path.Combine(dir, "NightShimmer") : null;
        iniPath = folder != null ? Path.Combine(folder, "settings.ini") : null;

        try
        {
            if (folder != null)
            {
                string pb = Path.Combine(folder, "bokeh.png");
                string pg = Path.Combine(folder, "glint.png");
                if (File.Exists(pb)) bokehSprite = new CustomSprite(pb, new SizeF(16, 16), new PointF(0, 0));
                if (File.Exists(pg)) glintSprite = new CustomSprite(pg, new SizeF(16, 16), new PointF(0, 0));
            }
        }
        catch (Exception ex)
        {
            Notification.Show("~r~NightShimmer loi anh: " + ex.Message);
        }

        if (bokehSprite == null && glintSprite == null)
        {
            loadFailed = true;
            Notification.Show("~r~NightShimmer: khong tim thay bokeh.png / glint.png trong thu muc NightShimmer");
            return;
        }

        LoadIni();
        Notification.Show("NightShimmer da nap xong");
    }

    // ---------- doc settings.ini ----------
    static bool ParseBool(string v, bool def)
    {
        string s = v.Trim().ToLowerInvariant();
        if (s == "1" || s == "true" || s == "yes" || s == "on") return true;
        if (s == "0" || s == "false" || s == "no" || s == "off") return false;
        return def;
    }

    static void Apply(Cfg c, string key, string val)
    {
        float f;
        int n;
        CultureInfo ci = CultureInfo.InvariantCulture;
        switch (key.ToLowerInvariant())
        {
            case "strength": if (float.TryParse(val, NumberStyles.Float, ci, out f)) c.Strength = Math.Max(0f, Math.Min(2f, f)); break;
            case "bokehcount": if (int.TryParse(val, NumberStyles.Integer, ci, out n)) c.BokehCount = Math.Max(0, Math.Min(150, n)); break;
            case "bokehsize": if (float.TryParse(val, NumberStyles.Float, ci, out f)) c.BokehSize = Math.Max(4f, Math.Min(150f, f)); break;
            case "glintcount": if (int.TryParse(val, NumberStyles.Integer, ci, out n)) c.GlintCount = Math.Max(0, Math.Min(80, n)); break;
            case "glintsize": if (float.TryParse(val, NumberStyles.Float, ci, out f)) c.GlintSize = Math.Max(4f, Math.Min(120f, f)); break;
            case "drift": if (float.TryParse(val, NumberStyles.Float, ci, out f)) c.Drift = Math.Max(0f, Math.Min(80f, f)); break;
            case "twinklespeed": if (float.TryParse(val, NumberStyles.Float, ci, out f)) c.TwinkleSpeed = Math.Max(0.1f, Math.Min(5f, f)); break;
            case "palette": if (int.TryParse(val, NumberStyles.Integer, ci, out n)) c.Palette = Math.Max(0, Math.Min(3, n)); break;
            case "bandtop": if (float.TryParse(val, NumberStyles.Float, ci, out f)) c.BandTop = Math.Max(0f, Math.Min(0.9f, f)); break;
            case "bandbottom": if (float.TryParse(val, NumberStyles.Float, ci, out f)) c.BandBottom = Math.Max(0.1f, Math.Min(1f, f)); break;
            case "onlyatnight": c.OnlyAtNight = ParseBool(val, c.OnlyAtNight); break;
            case "forcenight": c.ForceNight = ParseBool(val, c.ForceNight); break;
            case "rainboost": if (float.TryParse(val, NumberStyles.Float, ci, out f)) c.RainBoost = Math.Max(0f, Math.Min(2f, f)); break;
            case "fadespeed": if (float.TryParse(val, NumberStyles.Float, ci, out f)) c.FadeSpeed = Math.Max(0.05f, Math.Min(5f, f)); break;
            case "hideininterior": c.HideInInterior = ParseBool(val, c.HideInInterior); break;
        }
    }

    void LoadIni()
    {
        if (iniPath == null || !File.Exists(iniPath)) return;
        try
        {
            Cfg c = new Cfg();
            foreach (string raw in File.ReadAllLines(iniPath))
            {
                string line = raw;
                int hash = line.IndexOf('#');
                if (hash >= 0) line = line.Substring(0, hash);
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                Apply(c, line.Substring(0, eq).Trim(), line.Substring(eq + 1).Trim());
            }
            if (c.BandBottom < c.BandTop + 0.1f) c.BandBottom = c.BandTop + 0.1f;
            cfg = c;
            iniStamp = File.GetLastWriteTimeUtc(iniPath);
        }
        catch { }
    }

    void OnKeyUp(object sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.F7) return;
        enabled = !enabled;
        Notification.Show("NightShimmer: " + (enabled ? "~g~BAT" : "~r~TAT"));
    }

    float Rand(float a, float b) { return a + (float)rng.NextDouble() * (b - a); }

    static float Smooth(float e0, float e1, float x)
    {
        float t = Math.Max(0f, Math.Min(1f, (x - e0) / (e1 - e0)));
        return t * t * (3f - 2f * t);
    }

    // chon mau theo bang mau
    void PickColor(int palette, out int r, out int g, out int b)
    {
        int p = palette;
        if (p == 0)
        {
            int k = rng.Next(10);
            if (k < 4) p = 1;          // vang am
            else if (k < 6) p = 4;     // hong
            else if (k < 8) p = 2;     // xanh lanh
            else p = 3;                // trang
        }
        switch (p)
        {
            case 1: r = 255; g = 196; b = 120; break;   // vang am
            case 2: r = 130; g = 205; b = 255; break;   // xanh lanh
            case 3: r = 255; g = 238; b = 220; break;   // trang am
            case 4: r = 255; g = 130; b = 180; break;   // hong
            default: r = 255; g = 255; b = 255; break;
        }
    }

    Bokeh NewBokeh(Cfg c, float W, float H, float k)
    {
        Bokeh b = new Bokeh();
        b.R = Rand(0.35f, 1.0f) * c.BokehSize * k;
        b.X = Rand(0f, W);
        b.Y = Rand(c.BandTop, c.BandBottom) * H;
        b.Phase = Rand(0f, 6.2832f);
        b.Freq = Rand(0.25f, 0.9f);
        b.Alpha = Rand(0.05f, 0.12f);
        float ang = Rand(0f, 6.2832f);
        float sp = Rand(0.3f, 1f) * c.Drift * k;
        b.VX = (float)Math.Cos(ang) * sp;
        b.VY = (float)Math.Sin(ang) * sp * 0.6f;
        PickColor(c.Palette, out b.Cr, out b.Cg, out b.Cb);
        return b;
    }

    Glint NewGlint(Cfg c, float W, float H, float k)
    {
        Glint g = new Glint();
        g.R = Rand(0.4f, 1.0f) * c.GlintSize * k;
        g.X = Rand(0f, W);
        g.Y = Rand(c.BandTop, c.BandBottom) * H;
        g.Age = -Rand(0f, 1.5f);          // so am = cho truoc khi loe
        g.Life = Rand(0.45f, 1.3f);
        g.Alpha = Rand(0.35f, 0.65f);
        PickColor(c.Palette == 0 ? 3 : c.Palette, out g.Cr, out g.Cg, out g.Cb);
        return g;
    }

    void OnTick(object sender, EventArgs e)
    {
        if (loadFailed || !enabled) return;

        float dt = Game.LastFrameTime;
        if (dt <= 0f || dt > 0.25f) dt = 0.016f;

        reloadTimer += dt;
        if (reloadTimer > 2f)
        {
            reloadTimer = 0f;
            try
            {
                if (iniPath != null && File.Exists(iniPath) && File.GetLastWriteTimeUtc(iniPath) != iniStamp)
                    LoadIni();
            }
            catch { }
        }

        Cfg c = cfg;

        // ---- ban dem? ----
        float h = (float)World.CurrentTimeOfDay.TotalHours;
        float day = Smooth(5f, 7f, h) * (1f - Smooth(19f, 21f, h));
        float target = c.ForceNight ? 1f : (c.OnlyAtNight ? 1f - day : 1f);

        float speed = c.FadeSpeed;
        Ped ped = Game.Player.Character;
        if (c.HideInInterior && ped != null && ped.Exists()
            && Function.Call<int>(Hash.GET_INTERIOR_FROM_ENTITY, ped.Handle) != 0)
        {
            target = 0f;
            speed = c.FadeSpeed * 3f;
        }

        if (level < target) level = Math.Min(target, level + dt * speed);
        else level = Math.Max(target, level - dt * speed);
        if (level <= 0.004f) return;

        float rain = Function.Call<float>(Hash.GET_RAIN_LEVEL);
        if (rain < 0f) rain = 0f;
        if (rain > 1f) rain = 1f;

        float W = GTA.UI.Screen.Width, H = GTA.UI.Screen.Height;
        float k = H / 1080f;
        clock += dt * c.TwinkleSpeed;
        float strength = c.Strength * level * (1f + c.RainBoost * rain * 0.6f);
        float sizeMul = 1f + c.RainBoost * rain * 0.4f;

        // ---- giu dung so luong ----
        while (bokehs.Count < c.BokehCount) bokehs.Add(NewBokeh(c, W, H, k));
        while (bokehs.Count > c.BokehCount) bokehs.RemoveAt(bokehs.Count - 1);
        while (glints.Count < c.GlintCount) glints.Add(NewGlint(c, W, H, k));
        while (glints.Count > c.GlintCount) glints.RemoveAt(glints.Count - 1);

        // ---- dom sang mo (bokeh): troi cham, nhap nhay nhe ----
        if (bokehSprite != null)
        {
            for (int i = 0; i < bokehs.Count; i++)
            {
                Bokeh b = bokehs[i];
                b.X += b.VX * dt;
                b.Y += b.VY * dt;
                float margin = b.R * 2f;
                if (b.X < -margin) b.X = W + margin; else if (b.X > W + margin) b.X = -margin;
                float top = c.BandTop * H, bot = c.BandBottom * H;
                if (b.Y < top) { b.Y = top; b.VY = Math.Abs(b.VY); }
                else if (b.Y > bot) { b.Y = bot; b.VY = -Math.Abs(b.VY); }

                float tw = 0.55f + 0.45f * (float)Math.Sin(clock * b.Freq * 6.2832f + b.Phase);
                float a = b.Alpha * tw * strength;
                if (a <= 0.004f) continue;
                float r = b.R * sizeMul;
                bokehSprite.Size = new SizeF(r * 2f, r * 2f);
                bokehSprite.Position = new PointF(b.X - r, b.Y - r);
                bokehSprite.Color = Color.FromArgb((int)(Math.Min(1f, a) * 255f), b.Cr, b.Cg, b.Cb);
                bokehSprite.Draw();
            }
        }

        // ---- tia sao loe len roi tat ----
        if (glintSprite != null)
        {
            for (int i = 0; i < glints.Count; i++)
            {
                Glint g = glints[i];
                g.Age += dt;
                if (g.Age >= g.Life)
                {
                    glints[i] = NewGlint(c, W, H, k);
                    continue;
                }
                if (g.Age < 0f) continue;

                float u = g.Age / g.Life;
                float env = (float)Math.Sin(u * 3.14159f);
                env = env * env;
                float a = g.Alpha * env * strength;
                if (a <= 0.004f) continue;
                float r = g.R * (0.7f + 0.3f * env) * sizeMul;
                glintSprite.Size = new SizeF(r * 2f, r * 2f);
                glintSprite.Position = new PointF(g.X - r, g.Y - r);
                glintSprite.Color = Color.FromArgb((int)(Math.Min(1f, a) * 255f), g.Cr, g.Cg, g.Cb);
                glintSprite.Draw();
            }
        }
    }
}
