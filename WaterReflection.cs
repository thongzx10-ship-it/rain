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
/// WaterReflection - mo phong bong phan chieu cua nhan vat va xe tren mat nuoc va mat duong uot.
/// Cach lam: lay anh doi xung cua vat the qua mat phang (mat nuoc / mat dat), roi chieu anh do len mat phang
/// theo duong nhin tu camera (dung quy luat phan chieu phang). Vat the duoc xap xi bang vai khoi hop xep chong
/// (than xe, buong lai / nguoi: chan, than, dau), moi khoi ve thanh da giac mo 3D co chieu sau.
/// Co gon song, mo dan theo do cao va khoang cach, manh hon khi troi mua / tren mat nuoc.
/// Chinh thong so bang WaterReflection/settings.ini (tu nap lai moi 2 giay).
/// </summary>
public class WaterReflection : Script
{
    class Cfg
    {
        public float Strength       = 1.0f;    // do dam chung (0 = tat, 1.5 = dam)
        public float MaxDistance    = 45f;     // chi ve cho vat the trong ban kinh nay (m)
        public int   MaxEntities    = 10;      // so vat the toi da
        public bool  Vehicles       = true;    // phan chieu xe
        public bool  Peds           = true;    // phan chieu nguoi (ke ca nhan vat cua ban)
        public bool  Water          = true;    // phan chieu tren mat nuoc (song, bien, ho boi)
        public bool  WetRoad        = true;    // phan chieu tren mat duong uot khi troi mua
        public bool  ForceWet       = false;   // true = duong luon uot (de thu khi troi nang)
        public float RoadDryTime    = 150f;    // sau khi tanh mua duong con uot bao nhieu giay
        public float Ripple         = 0.04f;   // do gon song (m), 0 = mat phang tinh
        public int   ObjR           = 40;      // mau co ban cua phan chieu (truoc khi pha mau troi): xe va nguoi
        public int   ObjG           = 44;
        public int   ObjB           = 52;
        public int   TintR          = 120;     // mau troi pha vao phan chieu
        public int   TintG          = 135;
        public int   TintB          = 150;
        public float TintMix        = 0.35f;   // 0 = giu nguyen mau vat the, 1 = chi con mau troi
        public int   BaseAlpha      = 105;     // do dam toi da cua phan chieu (0-255)
    }

    static readonly Hash H_DRAW_POLY = (Hash)0xAC26716048436851UL;
    static readonly Hash H_GET_WATER_HEIGHT = (Hash)0xF6829842C06AE524UL;
    static readonly Hash H_GET_MODEL_DIMENSIONS = (Hash)0x03E8D3D5F549087AUL;

    // khoi hop (toa do cuc bo): x0,x1,y0,y1,z0,z1
    struct Slab
    {
        public float X0, X1, Y0, Y1, Z0, Z1;
        public float AlphaMul;
    }

    Cfg cfg = new Cfg();
    string iniPath;
    DateTime iniStamp = DateTime.MinValue;
    float reloadTimer;
    float wetness;
    float clock;
    bool enabled = true;

    public WaterReflection()
    {
        Tick += OnTick;
        KeyUp += OnKeyUp;

        string dir = null;
        try { dir = Path.GetDirectoryName(Filename); } catch { }
        iniPath = dir != null ? Path.Combine(Path.Combine(dir, "WaterReflection"), "settings.ini") : null;
        LoadIni();
        try { wetness = Math.Max(0f, Math.Min(1f, Function.Call<float>(Hash.GET_RAIN_LEVEL))); } catch { }
        Notification.Show("WaterReflection da nap xong");
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
            case "maxdistance": if (float.TryParse(val, NumberStyles.Float, ci, out f)) c.MaxDistance = Math.Max(5f, Math.Min(150f, f)); break;
            case "maxentities": if (int.TryParse(val, NumberStyles.Integer, ci, out n)) c.MaxEntities = Math.Max(1, Math.Min(40, n)); break;
            case "vehicles": c.Vehicles = ParseBool(val, c.Vehicles); break;
            case "peds": c.Peds = ParseBool(val, c.Peds); break;
            case "water": c.Water = ParseBool(val, c.Water); break;
            case "wetroad": c.WetRoad = ParseBool(val, c.WetRoad); break;
            case "forcewet": c.ForceWet = ParseBool(val, c.ForceWet); break;
            case "roaddrytime": if (float.TryParse(val, NumberStyles.Float, ci, out f)) c.RoadDryTime = Math.Max(5f, Math.Min(1800f, f)); break;
            case "ripple": if (float.TryParse(val, NumberStyles.Float, ci, out f)) c.Ripple = Math.Max(0f, Math.Min(0.3f, f)); break;
            case "objr": if (int.TryParse(val, NumberStyles.Integer, ci, out n)) c.ObjR = Math.Max(0, Math.Min(255, n)); break;
            case "objg": if (int.TryParse(val, NumberStyles.Integer, ci, out n)) c.ObjG = Math.Max(0, Math.Min(255, n)); break;
            case "objb": if (int.TryParse(val, NumberStyles.Integer, ci, out n)) c.ObjB = Math.Max(0, Math.Min(255, n)); break;
            case "tintr": if (int.TryParse(val, NumberStyles.Integer, ci, out n)) c.TintR = Math.Max(0, Math.Min(255, n)); break;
            case "tintg": if (int.TryParse(val, NumberStyles.Integer, ci, out n)) c.TintG = Math.Max(0, Math.Min(255, n)); break;
            case "tintb": if (int.TryParse(val, NumberStyles.Integer, ci, out n)) c.TintB = Math.Max(0, Math.Min(255, n)); break;
            case "tintmix": if (float.TryParse(val, NumberStyles.Float, ci, out f)) c.TintMix = Math.Max(0f, Math.Min(1f, f)); break;
            case "basealpha": if (int.TryParse(val, NumberStyles.Integer, ci, out n)) c.BaseAlpha = Math.Max(10, Math.Min(255, n)); break;
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
        if (e.KeyCode != Keys.F6) return;
        enabled = !enabled;
        Notification.Show("WaterReflection: " + (enabled ? "~g~BAT" : "~r~TAT"));
    }

    // ---------- hinh hoc ----------
    // chieu diem P qua mat phang z = g tu camera C: lay anh doi xung roi tim giao diem tia nhin voi mat phang
    static bool Project(Vector3 p, Vector3 cam, float g, out Vector2 q)
    {
        q = new Vector2(0f, 0f);
        float mz = 2f * g - p.Z;                 // anh doi xung duoi mat phang
        float den = mz - cam.Z;
        if (den > -0.01f) return false;
        float t = (g - cam.Z) / den;
        if (t <= 0f || t >= 1f) return false;
        q = new Vector2(cam.X + t * (p.X - cam.X), cam.Y + t * (p.Y - cam.Y));
        return true;
    }

    static float Cross(Vector2 o, Vector2 a, Vector2 b)
    {
        return (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
    }

    // bao loi (monotone chain)
    static List<Vector2> Hull(List<Vector2> pts)
    {
        pts.Sort(delegate (Vector2 a, Vector2 b)
        {
            int c = a.X.CompareTo(b.X);
            return c != 0 ? c : a.Y.CompareTo(b.Y);
        });
        List<Vector2> h = new List<Vector2>();
        for (int i = 0; i < pts.Count; i++)
        {
            while (h.Count >= 2 && Cross(h[h.Count - 2], h[h.Count - 1], pts[i]) <= 0f) h.RemoveAt(h.Count - 1);
            h.Add(pts[i]);
        }
        int lower = h.Count + 1;
        for (int i = pts.Count - 2; i >= 0; i--)
        {
            while (h.Count >= lower && Cross(h[h.Count - 2], h[h.Count - 1], pts[i]) <= 0f) h.RemoveAt(h.Count - 1);
            h.Add(pts[i]);
        }
        if (h.Count > 1) h.RemoveAt(h.Count - 1);
        return h;
    }

    void Tri(Vector2 a, Vector2 b, Vector2 c, float z, int r, int g, int bl, int al)
    {
        // ve ca hai chieu cuon de khong bi cat mat sau
        Function.Call(H_DRAW_POLY, a.X, a.Y, z, b.X, b.Y, z, c.X, c.Y, z, r, g, bl, al);
        Function.Call(H_DRAW_POLY, a.X, a.Y, z, c.X, c.Y, z, b.X, b.Y, z, r, g, bl, al);
    }

    // ve phan chieu cua mot vat the (danh sach khoi hop) len mat phang
    void DrawSlabs(Entity ent, Slab[] slabs, Vector3 cam, float plane, float rippleAmp, int r, int g, int b, float alpha)
    {
        float zDraw = plane + 0.02f;
        for (int s = 0; s < slabs.Length; s++)
        {
            Slab sl = slabs[s];
            int al = (int)Math.Max(0f, Math.Min(255f, alpha * sl.AlphaMul));
            if (al < 3) continue;

            List<Vector2> pts = new List<Vector2>(8);
            bool ok = true;
            float[] xs = { sl.X0, sl.X1 };
            float[] ys = { sl.Y0, sl.Y1 };
            float[] zs = { sl.Z0, sl.Z1 };
            for (int ix = 0; ix < 2 && ok; ix++)
                for (int iy = 0; iy < 2 && ok; iy++)
                    for (int iz = 0; iz < 2 && ok; iz++)
                    {
                        Vector3 wp = ent.GetOffsetPosition(new Vector3(xs[ix], ys[iy], zs[iz]));
                        Vector2 q;
                        if (!Project(wp, cam, plane, out q)) { ok = false; break; }
                        if (rippleAmp > 0f)
                        {
                            q.X += (float)Math.Sin(clock * 3.1f + q.Y * 2.3f) * rippleAmp;
                            q.Y += (float)Math.Sin(clock * 2.7f + q.X * 2.1f) * rippleAmp;
                        }
                        pts.Add(q);
                    }
            if (!ok || pts.Count < 3) continue;

            List<Vector2> hull = Hull(pts);
            if (hull.Count < 3) continue;
            for (int i = 1; i + 1 < hull.Count; i++)
                Tri(hull[0], hull[i], hull[i + 1], zDraw, r, g, b, al);
        }
    }

    // thanh phan xe: than duoi / than tren / cabin / noc
    static Slab[] CarSlabs(Vector3 mn, Vector3 mx)
    {
        float L = mx.Y - mn.Y;
        float h = mx.Z - mn.Z;
        float w = 0.97f;
        Slab[] r = new Slab[4];
        r[0] = new Slab { X0 = mn.X * w, X1 = mx.X * w, Y0 = mn.Y, Y1 = mx.Y, Z0 = mn.Z, Z1 = mn.Z + 0.30f * h, AlphaMul = 1.00f };
        r[1] = new Slab { X0 = mn.X * 0.93f, X1 = mx.X * 0.93f, Y0 = mn.Y + 0.03f * L, Y1 = mx.Y - 0.03f * L, Z0 = mn.Z + 0.30f * h, Z1 = mn.Z + 0.55f * h, AlphaMul = 0.72f };
        r[2] = new Slab { X0 = mn.X * 0.80f, X1 = mx.X * 0.80f, Y0 = mn.Y + 0.28f * L, Y1 = mx.Y - 0.30f * L, Z0 = mn.Z + 0.55f * h, Z1 = mn.Z + 0.80f * h, AlphaMul = 0.46f };
        r[3] = new Slab { X0 = mn.X * 0.74f, X1 = mx.X * 0.74f, Y0 = mn.Y + 0.34f * L, Y1 = mx.Y - 0.36f * L, Z0 = mn.Z + 0.80f * h, Z1 = mx.Z, AlphaMul = 0.28f };
        return r;
    }

    // thanh phan nguoi (toa do tuong doi voi tam nguoi, chan o khoang z = -1.0)
    static readonly Slab[] PedSlabs =
    {
        new Slab { X0 = -0.17f, X1 = 0.17f, Y0 = -0.12f, Y1 = 0.12f, Z0 = -1.00f, Z1 = -0.10f, AlphaMul = 1.00f },
        new Slab { X0 = -0.26f, X1 = 0.26f, Y0 = -0.14f, Y1 = 0.14f, Z0 = -0.10f, Z1 = 0.50f, AlphaMul = 0.62f },
        new Slab { X0 = -0.11f, X1 = 0.11f, Y0 = -0.11f, Y1 = 0.11f, Z0 = 0.50f, Z1 = 0.78f, AlphaMul = 0.34f }
    };

    float FadeByDistance(float dist)
    {
        float t = 1f - dist / Math.Max(1f, cfg.MaxDistance);
        if (t <= 0f) return 0f;
        return t * t * (3f - 2f * t);
    }

    // =====================================================================
    void OnTick(object sender, EventArgs e)
    {
        if (!enabled) return;

        float dt = Game.LastFrameTime;
        if (dt <= 0f || dt > 0.25f) dt = 0.016f;
        clock += dt;

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
        if (c.Strength <= 0.001f) return;

        // ---- do uot cua mat duong ----
        float rain = Function.Call<float>(Hash.GET_RAIN_LEVEL);
        if (rain < 0f) rain = 0f;
        if (rain > 1f) rain = 1f;
        if (c.ForceWet) wetness = 1f;
        else if (rain > 0.05f) wetness = Math.Min(1f, Math.Max(wetness, rain) + rain * dt / 30f);
        else wetness = Math.Max(0f, wetness - dt / c.RoadDryTime);

        Ped player = Game.Player.Character;
        if (player == null || !player.Exists()) return;
        if (Function.Call<int>(Hash.GET_INTERIOR_FROM_ENTITY, player.Handle) != 0) return;

        Vector3 cam = GameplayCamera.Position;
        Vector3 center = player.Position;
        float maxD = c.MaxDistance;

        // ---- gom vat the ----
        List<Entity> list = new List<Entity>();
        List<float> dists = new List<float>();
        Vehicle pv = player.IsInVehicle() ? player.CurrentVehicle : null;

        if (c.Vehicles)
        {
            if (pv != null && pv.Exists()) { list.Add(pv); dists.Add(0f); }
            Vehicle[] vs = World.GetNearbyVehicles(center, maxD);
            if (vs != null)
                foreach (Vehicle v in vs)
                {
                    if (v == null || !v.Exists()) continue;
                    if (pv != null && v.Handle == pv.Handle) continue;
                    if (!v.IsOnScreen) continue;
                    list.Add(v); dists.Add(v.Position.DistanceTo(center));
                }
        }
        if (c.Peds)
        {
            Ped[] ps = World.GetNearbyPeds(center, maxD * 0.7f);
            if (ps != null)
                foreach (Ped p in ps)
                {
                    if (p == null || !p.Exists() || p.IsInVehicle() || p.IsDead || p.IsRagdoll) continue;
                    if (!p.IsOnScreen && p.Handle != player.Handle) continue;
                    if (p.Handle == player.Handle && pv != null) continue;
                    list.Add(p); dists.Add(p.Position.DistanceTo(center));
                }
        }

        // giu lai cac vat the gan nhat
        int count = list.Count;
        if (count == 0) return;
        int[] order = new int[count];
        for (int i = 0; i < count; i++) order[i] = i;
        Array.Sort(order, delegate (int a, int b) { return dists[a].CompareTo(dists[b]); });
        int use = Math.Min(count, c.MaxEntities);

        for (int n = 0; n < use; n++)
        {
            Entity ent = list[order[n]];
            float dist = dists[order[n]];
            Vector3 pos = ent.Position;

            // mat phang: mat dat; neu co nuoc o day thi mat nuoc
            float plane = pos.Z - ent.HeightAboveGround;
            bool water = false;
            if (c.Water)
            {
                OutputArgument oh = new OutputArgument();
                if (Function.Call<bool>(H_GET_WATER_HEIGHT, pos.X, pos.Y, pos.Z, oh))
                {
                    float wh = oh.GetResult<float>();
                    if (wh > plane - 0.3f && wh < pos.Z + 3f) { plane = wh; water = true; }
                }
            }
            float factor;
            if (water) factor = 1f;
            else if (c.WetRoad) factor = wetness;
            else factor = 0f;
            if (factor <= 0.02f) continue;

            float fade = FadeByDistance(dist);
            float alpha = c.BaseAlpha * c.Strength * factor * fade;
            if (alpha < 3f) continue;
            float ripple = c.Ripple * (water ? 1.6f : (0.4f + 0.6f * rain));

            int r = c.ObjR, g = c.ObjG, b = c.ObjB;
            Slab[] slabs;
            if (ent is Vehicle)
            {
                Vehicle v = (Vehicle)ent;
                OutputArgument omn = new OutputArgument();
                OutputArgument omx = new OutputArgument();
                Function.Call(H_GET_MODEL_DIMENSIONS, v.Model.Hash, omn, omx);
                Vector3 mn = omn.GetResult<Vector3>();
                Vector3 mx = omx.GetResult<Vector3>();
                slabs = CarSlabs(mn, mx);
            }
            else
            {
                slabs = PedSlabs;
            }
            // pha mau troi vao phan chieu
            r = (int)(r + (c.TintR - r) * c.TintMix);
            g = (int)(g + (c.TintG - g) * c.TintMix);
            b = (int)(b + (c.TintB - b) * c.TintMix);

            DrawSlabs(ent, slabs, cam, plane, ripple, r, g, b, alpha);
        }
    }
}
