using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using GTA;
using GTA.Math;
using GTA.Native;
using GTA.UI;

/// <summary>
/// CarSplash - nuoc bang len man hinh khi chay xe. Script rieng, khong can RainOnScreen.
/// - Chi bang khi dang chay xe (dung yen thi khong bang), chay cang nhanh bang cang manh
/// - Troi mua: duong uot ngay; tanh mua duong con uot them mot luc
/// - Xe khac vuot sat ben: nuoc bang len ve phia xe do
/// - Goc nhin thu nhat trong xe kin: nuoc bang len kinh chan gio, xe chay nhanh thi gio day giot len
/// - Chinh thong so bang CarSplash/settings.ini (tu nap lai moi 2 giay)
/// </summary>
public class CarSplash : Script
{
    class Cfg
    {
        public bool  OnlyWhenWet      = true;    // true = chi bang khi duong uot (dang mua / vua tanh mua)
        public bool  ForceWet         = false;   // true = duong luon uot (de thu khi troi nang)
        public float SplashPerMinute  = 8f;      // so dot nuoc bang manh moi phut (chay nhanh + duong uot het co)
        public float Mist             = 22f;     // so giot suong nuoc li ti bang len moi giay khi chay nhanh
        public float MinSpeed         = 2.5f;    // toc do toi thieu de co nuoc bang (m/s, 2.5 = khoang 9 km/h)
        public float RoadDryTime      = 150f;    // sau khi tanh mua duong con uot bao nhieu giay
        public bool  Overtake         = true;    // xe khac vuot sat ben thi nuoc bang len
        public float Power            = 1f;      // luc bang (0.5 = nhe, 2 = rat manh)
        public bool  WindshieldMode   = true;    // goc nhin thu nhat trong xe kin: bang len kinh chan gio (false = an)
        public float WindshieldLeft   = 0.06f;   // vung kinh chan gio, ti le man hinh (0 den 1)
        public float WindshieldRight  = 0.94f;
        public float WindshieldTop    = 0.04f;
        public float WindshieldBottom = 0.62f;
        public float WindshieldWind   = 1f;      // xe chay nhanh thi gio day giot len va toa sang hai ben (0 = tat)
        public float MinRadius        = 3f;      // giot nho nhat (px, tinh o man hinh 1080p)
        public float MaxRadius        = 30f;     // giot lon nhat
        public float SlideChance      = 0.5f;    // ti le giot dinh lai se chay thanh vet nuoc
        public float LifeMin          = 4f;      // thoi gian ton tai (giay)
        public float LifeMax          = 10f;
        public float FadeTime         = 2.5f;    // mo dan trong bao nhieu giay cuoi doi giot
        public float SpreadAmount     = 0.5f;    // luc mo dan giot toa rong them bay nhieu
        public float Opacity          = 0.95f;
        public int   MaxDrops         = 260;     // gioi han so giot cung luc
    }

    class Drop
    {
        public float X, Y, R, Age, Life;
        public float VX, VY;                 // van toc luc bang (px/giay)
        public float PhaseTimer, Speed, Fallen, Gone;
        public float Stretch = 1f;
        public bool Burst, Slider, Moving, Out, Dead;
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
    float wetness;
    float mistAcc;
    float scanTimer;
    float overtakeCd;
    float hideAlpha = 1f;

    public CarSplash()
    {
        Tick += OnTick;
        KeyUp += OnKeyUp;

        string dir = null;
        try { dir = Path.GetDirectoryName(Filename); } catch { }
        string folder = dir != null ? Path.Combine(dir, "CarSplash") : null;
        iniPath = folder != null ? Path.Combine(folder, "settings.ini") : null;

        List<string> files = new List<string>();
        try
        {
            if (folder != null && Directory.Exists(folder))
                files.AddRange(Directory.GetFiles(folder, "droplet*.png"));
            if (files.Count == 0 && dir != null)
            {
                // khong co anh rieng thi dung tam anh cua RainOnScreen
                string alt = Path.Combine(dir, "RainOnScreen");
                if (Directory.Exists(alt)) files.AddRange(Directory.GetFiles(alt, "droplet*.png"));
            }
        }
        catch { }
        files.Sort(StringComparer.OrdinalIgnoreCase);

        foreach (string f in files)
        {
            try { sprites.Add(new CustomSprite(f, new SizeF(16, 16), new PointF(0, 0))); }
            catch (Exception ex) { Notification.Show("~r~CarSplash loi anh " + Path.GetFileName(f) + ": " + ex.Message); }
        }

        if (sprites.Count == 0)
        {
            loadFailed = true;
            Notification.Show("~r~CarSplash: khong tim thay anh droplet*.png trong thu muc CarSplash (" + dir + ")");
            return;
        }

        LoadIni();
        // neu dang mua luc nap script thi duong da uot san
        try { wetness = Math.Max(0f, Math.Min(1f, Function.Call<float>(Hash.GET_RAIN_LEVEL))); } catch { }
        Notification.Show("CarSplash da nap xong (" + sprites.Count + " anh giot)");
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
            case "onlywhenwet": c.OnlyWhenWet = ParseBool(val, c.OnlyWhenWet); break;
            case "forcewet": c.ForceWet = ParseBool(val, c.ForceWet); break;
            case "splashperminute": if (float.TryParse(val, NumberStyles.Float, ci, out f)) c.SplashPerMinute = Math.Max(0f, Math.Min(120f, f)); break;
            case "mist": if (float.TryParse(val, NumberStyles.Float, ci, out f)) c.Mist = Math.Max(0f, Math.Min(200f, f)); break;
            case "minspeed": if (float.TryParse(val, NumberStyles.Float, ci, out f)) c.MinSpeed = Math.Max(0f, Math.Min(60f, f)); break;
            case "roaddrytime": if (float.TryParse(val, NumberStyles.Float, ci, out f)) c.RoadDryTime = Math.Max(5f, Math.Min(1800f, f)); break;
            case "overtake": c.Overtake = ParseBool(val, c.Overtake); break;
            case "power": if (float.TryParse(val, NumberStyles.Float, ci, out f)) c.Power = Math.Max(0.1f, Math.Min(4f, f)); break;
            case "windshieldmode": c.WindshieldMode = ParseBool(val, c.WindshieldMode); break;
            case "windshieldleft": if (float.TryParse(val, NumberStyles.Float, ci, out f)) c.WindshieldLeft = Math.Max(0f, Math.Min(0.9f, f)); break;
            case "windshieldright": if (float.TryParse(val, NumberStyles.Float, ci, out f)) c.WindshieldRight = Math.Max(0.1f, Math.Min(1f, f)); break;
            case "windshieldtop": if (float.TryParse(val, NumberStyles.Float, ci, out f)) c.WindshieldTop = Math.Max(0f, Math.Min(0.9f, f)); break;
            case "windshieldbottom": if (float.TryParse(val, NumberStyles.Float, ci, out f)) c.WindshieldBottom = Math.Max(0.1f, Math.Min(1f, f)); break;
            case "windshieldwind": if (float.TryParse(val, NumberStyles.Float, ci, out f)) c.WindshieldWind = Math.Max(0f, Math.Min(3f, f)); break;
            case "minradius": if (float.TryParse(val, NumberStyles.Float, ci, out f)) c.MinRadius = Math.Max(1f, f); break;
            case "maxradius": if (float.TryParse(val, NumberStyles.Float, ci, out f)) c.MaxRadius = Math.Max(2f, f); break;
            case "slidechance": if (float.TryParse(val, NumberStyles.Float, ci, out f)) c.SlideChance = Math.Max(0f, Math.Min(1f, f)); break;
            case "lifemin": if (float.TryParse(val, NumberStyles.Float, ci, out f)) c.LifeMin = Math.Max(1f, f); break;
            case "lifemax": if (float.TryParse(val, NumberStyles.Float, ci, out f)) c.LifeMax = Math.Max(1f, f); break;
            case "fadetime": if (float.TryParse(val, NumberStyles.Float, ci, out f)) c.FadeTime = Math.Max(0.2f, f); break;
            case "spreadamount": if (float.TryParse(val, NumberStyles.Float, ci, out f)) c.SpreadAmount = Math.Max(0f, Math.Min(3f, f)); break;
            case "opacity": if (float.TryParse(val, NumberStyles.Float, ci, out f)) c.Opacity = Math.Max(0.05f, Math.Min(1f, f)); break;
            case "maxdrops": if (int.TryParse(val, NumberStyles.Integer, ci, out n)) c.MaxDrops = Math.Max(10, Math.Min(1500, n)); break;
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
            if (c.MaxRadius < c.MinRadius + 1f) c.MaxRadius = c.MinRadius + 1f;
            cfg = c;
            iniStamp = File.GetLastWriteTimeUtc(iniPath);
        }
        catch { }
    }

    void OnKeyUp(object sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.F9) return;
        enabled = !enabled;
        if (!enabled) drops.Clear();
        Notification.Show("CarSplash: " + (enabled ? "~g~BAT" : "~r~TAT"));
    }

    float Rand(float a, float b) { return a + (float)rng.NextDouble() * (b - a); }

    // nuoc bang len: giot bay nhanh roi cham dan, dinh lai tren kinh / man hinh
    // cxFrac: vi tri tam dot bang theo chieu ngang vung hien thi (0 = trai, 1 = phai), am = ngau nhien
    void SpawnBurst(Cfg c, float k, float W, float H, bool windMode, float rl, float rr, float rt, float rb,
                    int count, float power, bool tiny, float cxFrac)
    {
        float x0 = windMode ? rl : 0f, x1 = windMode ? rr : W;
        float y0 = windMode ? rb - 0.30f * (rb - rt) : 0.55f * H;
        float y1 = windMode ? rb : H;
        float frac = cxFrac >= 0f ? cxFrac : Rand(0.15f, 0.85f);
        float cx = x0 + (x1 - x0) * Math.Max(0.05f, Math.Min(0.95f, frac));
        float spreadX = 0.14f * (x1 - x0);

        for (int n = 0; n < count && drops.Count < c.MaxDrops + 150; n++)
        {
            Drop d = new Drop();
            if (tiny)
            {
                d.R = Rand(c.MinRadius * 0.6f, c.MinRadius * 1.6f) * k;
                d.X = Rand(x0, x1);
                d.Life = Rand(1.5f, 4f);
            }
            else
            {
                float t = (float)Math.Pow(rng.NextDouble(), 2.0);
                d.R = (c.MinRadius + (c.MaxRadius * 0.6f - c.MinRadius) * t) * k;
                d.X = Math.Max(x0, Math.Min(x1, cx + Rand(-1f, 1f) * spreadX));
                d.Life = Rand(c.LifeMin, c.LifeMax);
            }
            d.Y = Rand(y0, y1);
            d.Age = 0.2f;
            d.Variant = rng.Next(sprites.Count);
            d.Burst = true;
            d.VY = -Rand(300f, 900f) * k * power;
            d.VX = ((d.X - W * 0.5f) / W * Rand(300f, 800f) + Rand(-150f, 150f)) * k * power;

            if (!tiny && rng.NextDouble() < c.SlideChance)
            {
                d.Slider = true;
                d.Moving = false;
                d.PhaseTimer = Rand(0.8f, 2.5f);
                d.Speed = Rand(70f, 170f) * k;
            }
            drops.Add(d);
        }
    }

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

        // ---- do uot mat duong ----
        float rain = Function.Call<float>(Hash.GET_RAIN_LEVEL);
        if (rain < 0f) rain = 0f;
        if (rain > 1f) rain = 1f;
        if (c.ForceWet || !c.OnlyWhenWet) wetness = 1f;
        else if (rain > 0.05f) wetness = Math.Min(1f, Math.Max(wetness, rain) + rain * dt / 30f);
        else wetness = Math.Max(0f, wetness - dt / c.RoadDryTime);

        // ---- dang ngoi tren xe nao, o goc nhin nao ----
        Ped ped = Game.Player.Character;
        bool valid = ped != null && ped.Exists();
        bool interior = valid && Function.Call<int>(Hash.GET_INTERIOR_FROM_ENTITY, ped.Handle) != 0;
        bool inVeh = valid && !interior && Function.Call<bool>(Hash.IS_PED_IN_ANY_VEHICLE, ped.Handle, false);
        Vehicle veh = inVeh ? ped.CurrentVehicle : null;
        bool active = veh != null && veh.Exists();
        float vs = active ? veh.Speed : 0f;
        float sf = Math.Min(1f, vs / 28f);   // toc do xe 0 den 1 (1 = khoang 100 km/h)

        bool windMode = false;
        bool hidden = !active;
        if (active)
        {
            int cls = Function.Call<int>(Hash.GET_VEHICLE_CLASS, veh.Handle);
            bool openAir = (cls == 8 || cls == 13 || cls == 14);   // xe may, xe dap, thuyen
            // goc nhin trong xe: 0-2 = goc thu ba, 3 = dien anh, 4 = goc nhin thu nhat
            bool firstPerson = Function.Call<int>(Hash.GET_FOLLOW_VEHICLE_CAM_VIEW_MODE) == 4;
            if (firstPerson && !openAir)
            {
                if (c.WindshieldMode) windMode = true; else hidden = true;
            }
        }

        // mo han nhanh khi xuong xe / vao nha, hien lai khi len xe
        float hideStep = dt / 0.5f;
        if (hidden) hideAlpha = Math.Max(0f, hideAlpha - hideStep);
        else hideAlpha = Math.Min(1f, hideAlpha + hideStep * 2f);
        if (hideAlpha <= 0f)
        {
            if (drops.Count > 0) drops.Clear();
            mistAcc = 0f;
            return;
        }

        // vung kinh chan gio (pixel)
        float rl = W * c.WindshieldLeft, rr = W * Math.Max(c.WindshieldRight, c.WindshieldLeft + 0.1f);
        float rt = H * c.WindshieldTop, rb = H * Math.Max(c.WindshieldBottom, c.WindshieldTop + 0.1f);

        // ---- sinh nuoc bang len: chi khi dang chay xe tren duong uot ----
        overtakeCd -= dt;
        scanTimer -= dt;
        if (!hidden && vs > c.MinSpeed && wetness > 0.1f)
        {
            float spd = Math.Min(1f, (vs - c.MinSpeed) / 20f);
            float wf = (wetness - 0.1f) / 0.9f;
            float pw = c.Power;

            // dot nuoc bang manh (nhu can qua vung nuoc), ngau nhien
            if (rng.NextDouble() < (c.SplashPerMinute / 60.0) * wf * (0.1 + 0.9 * spd) * dt)
                SpawnBurst(c, k, W, H, windMode, rl, rr, rt, rb,
                           (int)(Rand(14f, 34f) * (0.5f + 0.5f * spd)), (0.6f + 0.8f * spd) * pw, false, -1f);

            // suong nuoc li ti bang len lien tuc khi chay nhanh
            mistAcc += c.Mist * wf * spd * dt;
            int mistGuard = 0;
            while (mistAcc >= 1f && mistGuard++ < 20)
            {
                mistAcc -= 1f;
                SpawnBurst(c, k, W, H, windMode, rl, rr, rt, rb, 1, (0.6f + 0.6f * spd) * pw, true, -1f);
            }

            // xe khac vuot sat ben: nuoc bang len ve phia xe do
            if (c.Overtake && wf > 0.2f && scanTimer <= 0f)
            {
                scanTimer = 0.2f;
                if (overtakeCd <= 0f)
                {
                    Vehicle[] near = World.GetNearbyVehicles(ped.Position, 9f);
                    if (near != null)
                    {
                        foreach (Vehicle o in near)
                        {
                            if (o == null || !o.Exists() || o.Handle == veh.Handle) continue;
                            Vector3 rel = o.Position - veh.Position;
                            if (rel.Length() > 8f) continue;
                            float closing = (o.Velocity - veh.Velocity).Length();
                            if (closing < 6f) continue;
                            float side = Vector3.Dot(rel, veh.RightVector);
                            SpawnBurst(c, k, W, H, windMode, rl, rr, rt, rb,
                                       (int)(Rand(18f, 40f) * (0.5f + 0.5f * spd)), (1.0f + 0.6f * spd) * pw, false,
                                       side >= 0f ? 0.82f : 0.18f);
                            overtakeCd = 2.5f;
                            break;
                        }
                    }
                }
            }
        }

        // ---- cap nhat ----
        for (int i = 0; i < drops.Count; i++)
        {
            Drop d = drops[i];
            if (d.Dead) continue;

            // nam ngoai vung kinh chan gio: mo nhanh roi mat
            if (windMode && !d.Out)
            {
                float mg = 0.03f * H;
                if (d.X < rl - mg || d.X > rr + mg || d.Y < rt - mg || d.Y > rb + mg) d.Out = true;
            }
            if (d.Out)
            {
                d.Gone += dt / 0.25f;
                if (d.Gone >= 1f) { d.Dead = true; continue; }
            }

            // bay nhanh roi cham dan, dinh lai
            if (d.Burst)
            {
                d.X += d.VX * dt;
                d.Y += d.VY * dt;
                float drag = (float)Math.Exp(-4.5 * dt);
                d.VX *= drag;
                d.VY *= drag;
                d.Stretch = 1f + Math.Min(0.8f, Math.Abs(d.VY) / (1000f * k));
                if (Math.Abs(d.VX) + Math.Abs(d.VY) < 12f * k) { d.Burst = false; d.Stretch = 1f; }
                if (d.Y + d.R < 0f || d.X < -d.R || d.X > W + d.R) { d.Dead = true; continue; }
            }

            d.Age += dt;
            if (d.Age >= d.Life) { d.Dead = true; continue; }

            // xe chay nhanh tren kinh chan gio: gio day giot dang dinh lai thanh chay len
            if (!d.Burst && !d.Slider && windMode && c.WindshieldWind > 0f && sf > 0.25f
                && rng.NextDouble() < (0.4 * sf * c.WindshieldWind) * dt * (1.0 + d.R / (6.0 * k)))
            {
                d.Slider = true;
                d.Moving = false;
                d.PhaseTimer = Rand(0.2f, 0.8f);
                d.Speed = Rand(80f, 200f) * k;
            }

            if (d.Slider && !d.Burst)
            {
                d.PhaseTimer -= dt;
                if (d.PhaseTimer <= 0f)
                {
                    d.Moving = !d.Moving;
                    d.PhaseTimer = d.Moving ? Rand(0.4f, 1.4f) : Rand(0.1f, 0.6f);
                    d.Speed = Rand(70f, 170f) * k;
                }

                if (d.Moving)
                {
                    float dirY = (windMode && sf > 0.2f && c.WindshieldWind > 0f) ? -1f : 1f;
                    float accel = 1f + 1.5f * Math.Min(1f, d.Fallen / (0.5f * H));
                    float dy = d.Speed * accel * dt * (dirY < 0f ? (0.5f + 1.5f * sf * c.WindshieldWind) : 1f);
                    d.Y += dy * dirY;
                    d.Fallen += dy;
                    d.X += (float)Math.Sin(d.Age * 4f + d.Life) * 6f * k * dt;
                    if (dirY < 0f) d.X += (d.X - W * 0.5f) * 0.10f * sf * dt;
                    d.Stretch += (1.45f - d.Stretch) * Math.Min(1f, 8f * dt);
                }
                else
                {
                    d.Stretch += (1f - d.Stretch) * Math.Min(1f, 6f * dt);
                }

                if (d.Fallen >= 0.35f * H || d.Y - d.R > H || d.Y + d.R < 0f) d.Dead = true;
            }
        }

        drops.RemoveAll(delegate (Drop dd) { return dd.Dead; });

        // ---- ve ----
        for (int i = 0; i < drops.Count; i++)
        {
            Drop d = drops[i];

            float st = Math.Min(1f, d.Age / 0.25f);
            float em1 = st - 1f;
            float eb = 1f + 2.70158f * em1 * em1 * em1 + 1.70158f * em1 * em1;   // loe ra roi chung lai
            float fadeIn = Math.Min(1f, d.Age / 0.2f);
            float fo = Math.Min(1f, (d.Life - d.Age) / c.FadeTime);
            fo = fo * fo * (3f - 2f * fo);
            float fallFrac = Math.Min(1f, d.Fallen / (0.35f * H));
            float fall = 1f - fallFrac;
            fall = fall * fall * (3f - 2f * fall);

            // vien kinh chan gio mo dan de khong bi cat cung
            float edge = 1f;
            if (windMode)
            {
                float ex = 0.04f * W, ey = 0.06f * H;
                float f1 = Math.Max(0f, Math.Min(1f, (d.X - rl) / ex));
                float f2 = Math.Max(0f, Math.Min(1f, (rr - d.X) / ex));
                float f3 = Math.Max(0f, Math.Min(1f, (d.Y - rt) / ey));
                float f4 = Math.Max(0f, Math.Min(1f, (rb - d.Y) / ey));
                edge = Math.Min(Math.Min(f1, f2), Math.Min(f3, f4));
                edge = edge * edge * (3f - 2f * edge);
            }

            float a = Math.Min(fadeIn, fo) * fall * c.Opacity * hideAlpha * edge * (1f - d.Gone);
            if (a <= 0.01f) continue;

            float pop = (0.3f + 0.7f * eb) * (1f - 0.3f * fallFrac);
            float prog = 1f - Math.Min(1f, (d.Life - d.Age) / c.FadeTime);
            float spread = c.SpreadAmount * prog;
            float rx = d.R * pop * (1f + spread);
            float ry = d.R * pop * d.Stretch * (1f + 0.7f * spread);

            CustomSprite sp = sprites[d.Variant % sprites.Count];
            sp.Size = new SizeF(rx * 2f, ry * 2f);
            sp.Position = new PointF(d.X - rx, d.Y - ry);
            sp.Color = Color.FromArgb((int)(a * 255f), 255, 255, 255);
            sp.Draw();
        }
    }
}
