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
/// Rain On Screen v2 - giot mua dong tren man hinh.
/// - Nhieu giot hon, mua toi la giot hien day dac
/// - Nhieu kieu anh: moi file droplet*.png trong thu muc RainOnScreen se duoc dung ngau nhien
/// - Giot chay xuong se gop cac giot nho tren duong di (nhu nuoc that)
/// - Chinh thong so bang RainOnScreen/settings.ini, tu nap lai moi 2 giay (khong can bien dich lai)
/// </summary>
public class RainOnScreen : Script
{
    class Cfg
    {
        public float SpawnPerSecond = 45f;   // so giot moi giay khi mua toi da
        public int   MaxDrops       = 380;   // gioi han so giot cung luc
        public float MinRadius      = 2.5f;  // ban kinh nho nhat (px, tinh o man hinh 1080p)
        public float MaxRadius      = 22f;   // ban kinh lon nhat
        public float Growth         = 1.6f;  // toc do lon them khi dang mua
        public float SlideRadius    = 10f;   // lon hon muc nay thi bat dau chay xuong
        public float SlideSpeed     = 1.5f;  // he so toc do chay xuong
        public float LifeMin        = 8f;    // thoi gian ton tai (giay)
        public float LifeMax        = 24f;
        public float DryingSpeed    = 3f;    // tanh mua thi kho nhanh gap may lan
        public float Opacity        = 0.95f;
        public float RainResponse   = 0.6f;  // nho hon = mua nhe van ra nhieu giot
        public bool  Merge          = true;  // giot chay xuong gop giot nho
        public bool  ForceRain      = false; // true = luon mua (de thu khi troi nang)
        public float FallChance     = 0.75f; // ti le giot se roi xuong (0 den 1)
        public float FallSpeed      = 1f;    // toc do roi cua cac giot nay
        public float FallDelayMax   = 4f;    // giot bam tren kinh toi da bao lau (giay) roi moi truot
        public float FadeTime       = 3.5f;  // thoi gian mo dan khi het tuoi tho (giay)
        public float FadeDistance   = 0.5f;  // giot roi het bay nhieu lan chieu cao man hinh thi mo hoan toan
        public float SplatTime      = 0.3f;  // thoi gian giot loe ra luc moi xuat hien (giay)
        public float SpreadAmount   = 0.6f;  // luc mo dan giot toa rong them bay nhieu (0.6 = +60%)
        public float SpreadFlatten  = 0.35f; // do det/loe ngang luc moi xuat hien (0 = khong det)
        public bool  HideInVehicle  = true;  // vao o to thi giot nuoc mat di
        public bool  ShowOnBikes    = true;  // xe may / xe dap / thuyen (ngoai troi) thi van hien giot
        public float HideFadeTime   = 0.6f;  // thoi gian giot mo han khi vao o to / vao nha (giay)
    }

    class Drop
    {
        public float X, Y, R, Age, Life, PhaseTimer, Speed, TrailAcc;
        public float Stretch = 1f;
        public bool Sliding, Moving, Dead, Faller;
        public float Fallen;
        public int Variant;
    }

    readonly List<Drop> drops = new List<Drop>();
    readonly List<CustomSprite> sprites = new List<CustomSprite>();
    readonly Random rng = new Random();
    Cfg cfg = new Cfg();
    string iniPath;
    DateTime iniStamp = DateTime.MinValue;
    float reloadTimer;
    bool enabled = true;
    bool loadFailed;
    float spawnAcc;
    float hideAlpha = 1f;   // 1 = hien, 0 = an hoan toan (vao o to / vao nha)

    public RainOnScreen()
    {
        Tick += OnTick;
        KeyUp += OnKeyUp;

        string dir = null;
        try { dir = Path.GetDirectoryName(Filename); } catch { }
        string folder = dir != null ? Path.Combine(dir, "RainOnScreen") : null;
        iniPath = folder != null ? Path.Combine(folder, "settings.ini") : null;

        // Gom tat ca anh giot nuoc: droplet.png, droplet2.png, droplet_xxx.png ...
        List<string> files = new List<string>();
        try
        {
            if (folder != null && Directory.Exists(folder))
                files.AddRange(Directory.GetFiles(folder, "droplet*.png"));
            if (dir != null)
            {
                string p = Path.Combine(dir, "droplet.png");
                if (File.Exists(p) && !files.Contains(p)) files.Add(p);
            }
            string fb = Path.Combine(Path.Combine("scripts", "RainOnScreen"), "droplet.png");
            if (files.Count == 0 && File.Exists(fb)) files.Add(fb);
        }
        catch { }
        files.Sort(StringComparer.OrdinalIgnoreCase);

        foreach (string f in files)
        {
            try { sprites.Add(new CustomSprite(f, new SizeF(16, 16), new PointF(0, 0))); }
            catch (Exception ex) { Notification.Show("~r~RainOnScreen loi anh " + Path.GetFileName(f) + ": " + ex.Message); }
        }

        if (sprites.Count == 0)
        {
            loadFailed = true;
            Notification.Show("~r~RainOnScreen: khong tim thay droplet.png (thu muc: " + dir + ")");
            return;
        }

        LoadIni();
        Notification.Show("RainOnScreen v2 da nap xong (" + sprites.Count + " anh giot)");
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
            case "spawnpersecond": if (float.TryParse(val, NumberStyles.Float, ci, out f)) c.SpawnPerSecond = Math.Max(0f, f); break;
            case "maxdrops": if (int.TryParse(val, NumberStyles.Integer, ci, out n)) c.MaxDrops = Math.Max(10, Math.Min(2000, n)); break;
            case "minradius": if (float.TryParse(val, NumberStyles.Float, ci, out f)) c.MinRadius = Math.Max(1f, f); break;
            case "maxradius": if (float.TryParse(val, NumberStyles.Float, ci, out f)) c.MaxRadius = Math.Max(2f, f); break;
            case "growth": if (float.TryParse(val, NumberStyles.Float, ci, out f)) c.Growth = Math.Max(0f, f); break;
            case "slideradius": if (float.TryParse(val, NumberStyles.Float, ci, out f)) c.SlideRadius = Math.Max(2f, f); break;
            case "slidespeed": if (float.TryParse(val, NumberStyles.Float, ci, out f)) c.SlideSpeed = Math.Max(0.1f, f); break;
            case "lifemin": if (float.TryParse(val, NumberStyles.Float, ci, out f)) c.LifeMin = Math.Max(1f, f); break;
            case "lifemax": if (float.TryParse(val, NumberStyles.Float, ci, out f)) c.LifeMax = Math.Max(1f, f); break;
            case "dryingspeed": if (float.TryParse(val, NumberStyles.Float, ci, out f)) c.DryingSpeed = Math.Max(0.5f, f); break;
            case "opacity": if (float.TryParse(val, NumberStyles.Float, ci, out f)) c.Opacity = Math.Max(0.05f, Math.Min(1f, f)); break;
            case "rainresponse": if (float.TryParse(val, NumberStyles.Float, ci, out f)) c.RainResponse = Math.Max(0.1f, Math.Min(2f, f)); break;
            case "fallchance": if (float.TryParse(val, NumberStyles.Float, ci, out f)) c.FallChance = Math.Max(0f, Math.Min(1f, f)); break;
            case "fallspeed": if (float.TryParse(val, NumberStyles.Float, ci, out f)) c.FallSpeed = Math.Max(0.1f, f); break;
            case "falldelaymax": if (float.TryParse(val, NumberStyles.Float, ci, out f)) c.FallDelayMax = Math.Max(0.3f, f); break;
            case "fadetime": if (float.TryParse(val, NumberStyles.Float, ci, out f)) c.FadeTime = Math.Max(0.2f, f); break;
            case "fadedistance": if (float.TryParse(val, NumberStyles.Float, ci, out f)) c.FadeDistance = Math.Max(0f, Math.Min(3f, f)); break;
            case "splattime": if (float.TryParse(val, NumberStyles.Float, ci, out f)) c.SplatTime = Math.Max(0.05f, f); break;
            case "spreadamount": if (float.TryParse(val, NumberStyles.Float, ci, out f)) c.SpreadAmount = Math.Max(0f, Math.Min(3f, f)); break;
            case "spreadflatten": if (float.TryParse(val, NumberStyles.Float, ci, out f)) c.SpreadFlatten = Math.Max(0f, Math.Min(1.5f, f)); break;
            case "hideinvehicle": c.HideInVehicle = ParseBool(val, c.HideInVehicle); break;
            case "showonbikes": c.ShowOnBikes = ParseBool(val, c.ShowOnBikes); break;
            case "hidefadetime": if (float.TryParse(val, NumberStyles.Float, ci, out f)) c.HideFadeTime = Math.Max(0.05f, Math.Min(10f, f)); break;
            case "merge": c.Merge = ParseBool(val, c.Merge); break;
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
            if (c.LifeMax < c.LifeMin) c.LifeMax = c.LifeMin;
            cfg = c;
            iniStamp = File.GetLastWriteTimeUtc(iniPath);
        }
        catch { }
    }

    void OnKeyUp(object sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.F10) return;
        enabled = !enabled;
        if (!enabled) drops.Clear();
        Notification.Show("Rain On Screen: " + (enabled ? "~g~BAT" : "~r~TAT"));
    }

    float Rand(float a, float b) { return a + (float)rng.NextDouble() * (b - a); }

    void OnTick(object sender, EventArgs e)
    {
        if (loadFailed || !enabled || sprites.Count == 0) return;

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
        float W = GTA.UI.Screen.Width, H = GTA.UI.Screen.Height;
        float k = H / 1080f;

        float rain = c.ForceRain ? 0.8f : Function.Call<float>(Hash.GET_RAIN_LEVEL);
        if (rain < 0f) rain = 0f;
        if (rain > 1f) rain = 1f;
        float inten = (float)Math.Pow(rain, c.RainResponse);

        Ped ped = Game.Player.Character;
        bool sheltered = ped == null || !ped.Exists()
                         || Function.Call<int>(Hash.GET_INTERIOR_FROM_ENTITY, ped.Handle) != 0;

        // vao o to thi giot nuoc mat di (tru xe may, xe dap, thuyen neu ShowOnBikes = true)
        if (!sheltered && c.HideInVehicle && Function.Call<bool>(Hash.IS_PED_IN_ANY_VEHICLE, ped.Handle, false))
        {
            bool openAir = false;
            if (c.ShowOnBikes)
            {
                Vehicle veh = ped.CurrentVehicle;
                if (veh != null && veh.Exists())
                {
                    int cls = Function.Call<int>(Hash.GET_VEHICLE_CLASS, veh.Handle);
                    openAir = (cls == 8 || cls == 13 || cls == 14);   // 8 = xe may, 13 = xe dap, 14 = thuyen
                }
            }
            if (!openAir) sheltered = true;
        }

        bool wet = rain > 0.03f && !sheltered;

        // mo han nhanh khi vao cho kin, hien lai khi ra ngoai
        float hideStep = dt / c.HideFadeTime;
        if (sheltered) hideAlpha = Math.Max(0f, hideAlpha - hideStep);
        else hideAlpha = Math.Min(1f, hideAlpha + hideStep * 2f);
        if (hideAlpha <= 0f)
        {
            if (drops.Count > 0) drops.Clear();
            spawnAcc = 0f;
            return;
        }

        // ---- sinh giot moi ----
        if (wet)
        {
            spawnAcc += inten * c.SpawnPerSecond * dt;
            int guard = 0;
            while (spawnAcc >= 1f && drops.Count < c.MaxDrops && guard++ < 60)
            {
                spawnAcc -= 1f;
                // mua cang to thi cang co nhieu giot lon, nhung phan lon van la giot nho
                float t = (float)Math.Pow(rng.NextDouble(), 3.2 - 1.6 * inten);
                Drop d = new Drop();
                d.R = (c.MinRadius + (c.MaxRadius - c.MinRadius) * 0.55f * t) * k;
                d.X = Rand(0f, W);
                d.Y = Rand(0f, H);
                d.Life = Rand(c.LifeMin, c.LifeMax);
                d.Variant = rng.Next(sprites.Count);
                if (rng.NextDouble() < c.FallChance)
                {
                    d.Faller = true;
                    d.Sliding = true;
                    d.Moving = false;
                    d.PhaseTimer = Rand(0.2f, c.FallDelayMax);
                    d.Speed = Rand(70f, 170f) * k * c.FallSpeed;
                }
                drops.Add(d);
            }
            if (drops.Count >= c.MaxDrops) spawnAcc = 0f;
        }
        else
        {
            spawnAcc = 0f;
        }

        float ageRate = wet ? 1f : c.DryingSpeed;
        List<Drop> spawned = null;

        // ---- cap nhat ----
        for (int i = 0; i < drops.Count; i++)
        {
            Drop d = drops[i];
            if (d.Dead) continue;

            d.Age += dt * ageRate;
            if (d.Age >= d.Life) { d.Dead = true; continue; }

            if (!d.Sliding)
            {
                if (wet && d.R < c.MaxRadius * k)
                    d.R += c.Growth * k * inten * dt * Rand(0.4f, 1.2f);

                if (d.R > c.SlideRadius * k && rng.NextDouble() < 0.5 * dt + 0.03 * dt * d.R / k)
                {
                    d.Sliding = true;
                    d.Moving = false;
                    d.PhaseTimer = Rand(0.1f, 0.4f);
                    d.Speed = Rand(60f, 140f) * k * c.SlideSpeed * (d.R / (c.SlideRadius * k));
                }
            }
            else
            {
                d.PhaseTimer -= dt;
                if (d.PhaseTimer <= 0f)
                {
                    d.Moving = !d.Moving;
                    d.PhaseTimer = d.Moving ? Rand(0.4f, 1.4f) : Rand(0.1f, 0.6f);
                    float sizeF = d.Faller ? c.FallSpeed : Math.Max(0.5f, d.R / (c.SlideRadius * k));
                    d.Speed = Rand(70f, 170f) * k * c.SlideSpeed * sizeF;
                }

                if (d.Moving)
                {
                    // cang roi lau cang nhanh (nhu trong luc)
                    float accel = 1f + 1.5f * Math.Min(1f, d.Fallen / (0.5f * H));
                    float dy = d.Speed * accel * dt;
                    d.Y += dy;
                    d.Fallen += dy;
                    d.X += (float)Math.Sin(d.Age * 4f + d.Life) * 6f * k * dt;
                    d.TrailAcc += dy;
                    if (!d.Faller) d.R -= 0.3f * k * dt;   // giot thuong mat nuoc dan
                    d.Stretch += (1.45f - d.Stretch) * Math.Min(1f, 8f * dt);

                    // chi giot du lon moi de lai vet giot nho phia sau
                    if (d.R > 4f * k && d.TrailAcc > d.R * 2.2f)
                    {
                        d.TrailAcc = 0f;
                        if (spawned == null) spawned = new List<Drop>();
                        Drop tr = new Drop();
                        tr.R = Math.Max(1.5f * k, d.R * Rand(0.16f, 0.30f));
                        tr.X = d.X + Rand(-1.5f, 1.5f) * k;
                        tr.Y = d.Y - d.R * 1.2f;
                        tr.Life = Rand(3f, 8f);
                        tr.Variant = rng.Next(sprites.Count);
                        spawned.Add(tr);
                    }
                }
                else
                {
                    d.Stretch += (1f - d.Stretch) * Math.Min(1f, 6f * dt);
                }

                if (d.Faller)
                {
                    // roi du xa thi mo hoan toan va bien mat
                    if (c.FadeDistance > 0f && d.Fallen >= c.FadeDistance * H) d.Dead = true;
                    if (d.Y - d.R > H) d.Dead = true;
                }
                else if (d.R < 2f * k || d.Y - d.R > H)
                {
                    d.Dead = true;
                }
            }
        }

        // ---- giot chay xuong gop cac giot nho tren duong di ----
        if (c.Merge)
        {
            for (int i = 0; i < drops.Count; i++)
            {
                Drop s = drops[i];
                if (s.Dead || !s.Sliding || !s.Moving) continue;
                for (int j = 0; j < drops.Count; j++)
                {
                    Drop o = drops[j];
                    if (o.Dead || o.Sliding || o == s) continue;
                    float dx = o.X - s.X, dy = o.Y - s.Y;
                    float reach = s.R * 0.85f + o.R;
                    if (dx * dx + dy * dy < reach * reach)
                    {
                        s.R = (float)Math.Sqrt(s.R * s.R + o.R * o.R * 0.8f);
                        float cap = c.MaxRadius * 1.5f * k;
                        if (s.R > cap) s.R = cap;
                        o.Dead = true;
                    }
                }
            }
        }

        if (spawned != null)
            foreach (Drop tr in spawned)
                if (drops.Count < c.MaxDrops + 120) drops.Add(tr);

        drops.RemoveAll(delegate (Drop dd) { return dd.Dead; });

        // ---- ve ----
        for (int i = 0; i < drops.Count; i++)
        {
            Drop d = drops[i];
            float fadeIn = Math.Min(1f, d.Age / 0.3f);
            float fo = Math.Min(1f, (d.Life - d.Age) / c.FadeTime);
            fo = fo * fo * (3f - 2f * fo);                       // mo dan muot khi het tuoi tho
            float fallFrac = (c.FadeDistance > 0f) ? Math.Min(1f, d.Fallen / (c.FadeDistance * H)) : 0f;
            float fall = 1f - fallFrac;
            fall = fall * fall * (3f - 2f * fall);               // mo dan muot khi dang roi
            float a = Math.Min(fadeIn, fo) * fall * c.Opacity * hideAlpha;
            if (a <= 0.01f) continue;

            // loe ra luc moi xuat hien: nho -> vuot qua mot chut -> chung lai (easeOutBack)
            float st = Math.Min(1f, d.Age / c.SplatTime);
            float em1 = st - 1f;
            float eb = 1f + 2.70158f * em1 * em1 * em1 + 1.70158f * em1 * em1;
            float pop = (0.3f + 0.7f * eb) * (1f - 0.3f * fallFrac);

            // luc mo dan: toa rong va det ra nhu vet nuoc loang
            float prog = 1f - Math.Min(1f, (d.Life - d.Age) / c.FadeTime);
            float spread = c.SpreadAmount * prog;
            float flat = c.SpreadFlatten * (1f - st);

            float rx = d.R * pop * (1f + flat) * (1f + spread);
            float ry = d.R * pop * d.Stretch * (1f - 0.4f * flat) * (1f + 0.7f * spread);

            CustomSprite sp = sprites[d.Variant % sprites.Count];
            sp.Size = new SizeF(rx * 2f, ry * 2f);
            sp.Position = new PointF(d.X - rx, d.Y - ry);
            sp.Color = Color.FromArgb((int)(a * 255f), 255, 255, 255);
            sp.Draw();
        }
    }
}
