using System;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using GTA;
using GTA.Native;
using GTA.UI;

/// <summary>
/// RainLook - script do hoa nhe khi troi mua (do toi tu viet, khong dung file cua mod nao khac).
/// Khi mua: canh toi va lanh hon, troi nang nuoc den hon, vien toi bon goc man hinh.
/// Hieu ung ve bang cac hinh chu nhat ban trong suot, tu mo dan khi het mua / vao nha.
/// Chinh thong so bang RainLook/settings.ini (tu nap lai moi 2 giay).
/// </summary>
public class RainLook : Script
{
    class Cfg
    {
        public float Tint            = 0.10f;   // do dam lop mau phu toan man hinh (0 den 0.5)
        public int   TintR           = 38;      // mau lop phu (xam xanh lanh)
        public int   TintG           = 54;
        public int   TintB           = 74;
        public float Vignette        = 0.38f;   // do dam vien toi bon goc (0 den 1)
        public float VignetteSize    = 0.20f;   // do rong vien toi, ti le chieu ngang man hinh
        public float SkyDarken       = 0.16f;   // lam toi phan tren man hinh (may mua)
        public float SkyHeight       = 0.45f;   // phan tren man hinh bi lam toi cao bao nhieu
        public float NightFactor     = 0.45f;   // ban dem hieu ung nhe di (1 = giu nguyen)
        public bool  OnlyWhenRaining = true;    // false = luc nao cung bat
        public float RainResponse    = 0.7f;    // nho hon = mua nhe cung ra hieu ung manh
        public float FadeSpeed       = 0.25f;   // toc do len / xuong theo mua (1 = 1 giay)
        public bool  HideInInterior  = true;    // vao nha thi tat
        public bool  ForceRain       = false;   // true = luon mua (de thu khi troi nang)
    }

    static readonly Hash H_DRAW_RECT = (Hash)0x3A618A217E5154F0;

    Cfg cfg = new Cfg();
    string iniPath;
    DateTime iniStamp = DateTime.MinValue;
    float reloadTimer;
    bool enabled = true;
    float level;   // muc hieu ung hien tai 0..1

    public RainLook()
    {
        Tick += OnTick;
        KeyUp += OnKeyUp;

        string dir = null;
        try { dir = Path.GetDirectoryName(Filename); } catch { }
        iniPath = dir != null ? Path.Combine(Path.Combine(dir, "RainLook"), "settings.ini") : null;
        LoadIni();
        Notification.Show("RainLook da nap xong");
    }

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
            case "tint": if (float.TryParse(val, NumberStyles.Float, ci, out f)) c.Tint = Math.Max(0f, Math.Min(0.5f, f)); break;
            case "tintr": if (int.TryParse(val, NumberStyles.Integer, ci, out n)) c.TintR = Math.Max(0, Math.Min(255, n)); break;
            case "tintg": if (int.TryParse(val, NumberStyles.Integer, ci, out n)) c.TintG = Math.Max(0, Math.Min(255, n)); break;
            case "tintb": if (int.TryParse(val, NumberStyles.Integer, ci, out n)) c.TintB = Math.Max(0, Math.Min(255, n)); break;
            case "vignette": if (float.TryParse(val, NumberStyles.Float, ci, out f)) c.Vignette = Math.Max(0f, Math.Min(1f, f)); break;
            case "vignettesize": if (float.TryParse(val, NumberStyles.Float, ci, out f)) c.VignetteSize = Math.Max(0.02f, Math.Min(0.5f, f)); break;
            case "skydarken": if (float.TryParse(val, NumberStyles.Float, ci, out f)) c.SkyDarken = Math.Max(0f, Math.Min(0.8f, f)); break;
            case "skyheight": if (float.TryParse(val, NumberStyles.Float, ci, out f)) c.SkyHeight = Math.Max(0.05f, Math.Min(1f, f)); break;
            case "nightfactor": if (float.TryParse(val, NumberStyles.Float, ci, out f)) c.NightFactor = Math.Max(0f, Math.Min(1f, f)); break;
            case "onlywhenraining": c.OnlyWhenRaining = ParseBool(val, c.OnlyWhenRaining); break;
            case "rainresponse": if (float.TryParse(val, NumberStyles.Float, ci, out f)) c.RainResponse = Math.Max(0.1f, Math.Min(2f, f)); break;
            case "fadespeed": if (float.TryParse(val, NumberStyles.Float, ci, out f)) c.FadeSpeed = Math.Max(0.05f, Math.Min(5f, f)); break;
            case "hideininterior": c.HideInInterior = ParseBool(val, c.HideInInterior); break;
            case "forcerain": c.ForceRain = ParseBool(val, c.ForceRain); break;
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
            cfg = c;
            iniStamp = File.GetLastWriteTimeUtc(iniPath);
        }
        catch { }
    }

    void OnKeyUp(object sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.F8) return;
        enabled = !enabled;
        Notification.Show("RainLook: " + (enabled ? "~g~BAT" : "~r~TAT"));
    }

    // toa do chuan hoa 0..1: tam (cx, cy), kich thuoc (w, h)
    static void Rect(float cx, float cy, float w, float h, int r, int g, int b, float alpha)
    {
        int a = (int)(alpha * 255f);
        if (a <= 0) return;
        if (a > 255) a = 255;
        Function.Call(H_DRAW_RECT, cx, cy, w, h, r, g, b, a);
    }

    static float Smooth(float e0, float e1, float x)
    {
        float t = Math.Max(0f, Math.Min(1f, (x - e0) / (e1 - e0)));
        return t * t * (3f - 2f * t);
    }

    void OnTick(object sender, EventArgs e)
    {
        if (!enabled) return;

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

        float rain = c.ForceRain ? 0.8f : Function.Call<float>(Hash.GET_RAIN_LEVEL);
        if (rain < 0f) rain = 0f;
        if (rain > 1f) rain = 1f;
        float target = c.OnlyWhenRaining ? (float)Math.Pow(rain, c.RainResponse) : 1f;
        if (rain <= 0.03f && c.OnlyWhenRaining) target = 0f;

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

        // ban dem hieu ung nhe di (troi da toi san)
        TimeSpan tod = World.CurrentTimeOfDay;
        float h = (float)tod.TotalHours;
        float day = Smooth(5f, 7f, h) * (1f - Smooth(19f, 21f, h));
        float nf = c.NightFactor + (1f - c.NightFactor) * day;

        float s = level * nf;
        if (s <= 0.003f) return;

        // 1) lop mau lanh phu toan man hinh
        Rect(0.5f, 0.5f, 1f, 1f, c.TintR, c.TintG, c.TintB, c.Tint * s);

        // 2) lam toi phan tren (may mua nang): cang len tren cang toi
        const int skyBands = 16;
        float bh = c.SkyHeight / skyBands;
        for (int i = 0; i < skyBands; i++)
        {
            float k = 1f - (i + 0.5f) / skyBands;
            float a = c.SkyDarken * s * k * (float)Math.Sqrt(k);
            Rect(0.5f, (i + 0.5f) * bh, 1.002f, bh + 0.001f, 10, 16, 26, a);
        }

        // 3) vien toi bon goc
        if (c.Vignette > 0.001f)
        {
            const int vb = 24;
            float aspect = (float)GTA.UI.Screen.Width / Math.Max(1, GTA.UI.Screen.Height);
            float bw = c.VignetteSize / vb;
            float bhh = bw * aspect;
            for (int i = 0; i < vb; i++)
            {
                float k = 1f - (i + 0.5f) / vb;
                float a = c.Vignette * s * k * k;
                float cx = (i + 0.5f) * bw;
                float cy = (i + 0.5f) * bhh;
                Rect(cx, 0.5f, bw + 0.001f, 1.002f, 0, 0, 0, a);               // trai
                Rect(1f - cx, 0.5f, bw + 0.001f, 1.002f, 0, 0, 0, a);          // phai
                Rect(0.5f, cy, 1.002f, bhh + 0.001f, 0, 0, 0, a);              // tren
                Rect(0.5f, 1f - cy, 1.002f, bhh + 0.001f, 0, 0, 0, a);         // duoi
            }
        }
    }
}
