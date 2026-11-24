using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Media;
using System.Text;
using System.Windows.Forms;
using GTA;
using GTA.Math;
using GTA.Native;
using GTA.UI;

/// <summary>
/// ExhaustPops (Dyza16) - ban viet lai tu ma nguon, giu nguyen moi tinh nang cu va them:
///  - Bang mau lua day du: 7 mau goc + cau vong + ngau nhien + 19 mau moi + mau tuy chinh (RGB)
///  - Cac muc Custom R / G / B ngay trong menu (phim F7), o mau xem truoc ben canh
///  - Hieu ung lua "Tu dong": co mau thi dung hieu ung an mau (veh_nitrous), khong mau thi veh_backfire
/// Khong doi cach dat file: scripts\ExhaustPops.dll, scripts\ExhaustPops.ini, scripts\ExhaustPops\Sounds\...
/// </summary>
public class ExhaustPops : Script
{
    // ---- ma ham game (giong ban goc) ----
    static readonly Hash H_REQUEST_NAMED_PTFX_ASSET = (Hash)0xB80D8756B4668AB6UL;
    static readonly Hash H_HAS_NAMED_PTFX_ASSET_LOADED = (Hash)0x8702416E512EC454UL;
    static readonly Hash H_GET_ENTITY_BONE_INDEX_BY_NAME = (Hash)0xFB71170B7E76ACBAUL;
    static readonly Hash H_GET_WORLD_POSITION_OF_ENTITY_BONE = (Hash)0x44A8FCB8ED227738UL;
    static readonly Hash H_GET_MODEL_DIMENSIONS = (Hash)0x03E8D3D5F549087AUL;
    static readonly Hash H_USE_PARTICLE_FX_ASSET = (Hash)0x6C38AF3693A69A91UL;
    static readonly Hash H_SET_PARTICLE_FX_NON_LOOPED_COLOUR = (Hash)0x26143A59EF48B262UL;
    static readonly Hash H_START_PARTICLE_FX_NON_LOOPED_ON_ENTITY = (Hash)0x0D53A3B8DA0809D2UL;
    static readonly Hash H_GET_CONTROL_NORMAL = (Hash)0xEC3C9B8D5327B563UL;
    static readonly Hash H_IS_CONTROL_JUST_PRESSED = (Hash)0x91AEF906BCA88877UL;
    static readonly Hash H_DISABLE_CONTROL_ACTION = (Hash)0xFE99B66D079CF6BCUL;
    static readonly Hash H_DRAW_LIGHT_WITH_RANGE = (Hash)0xF2A1B2771A01DBD4UL;
    static readonly Hash H_DRAW_RECT = (Hash)0x3A618A217E5154F0UL;
    static readonly Hash H_SET_TEXT_FONT = (Hash)0x66E0276CC5F6B9DAUL;
    static readonly Hash H_SET_TEXT_SCALE = (Hash)0x07C837F9A01C34C9UL;
    static readonly Hash H_SET_TEXT_COLOUR = (Hash)0xBE6B23FFA53FB442UL;
    static readonly Hash H_SET_TEXT_OUTLINE = (Hash)0x2513DFB0FB8400FEUL;
    static readonly Hash H_BEGIN_TEXT_COMMAND_DISPLAY_TEXT = (Hash)0x25FBB336DF1804CBUL;
    static readonly Hash H_ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME = (Hash)0x6C188BE134E074AAUL;
    static readonly Hash H_END_TEXT_COMMAND_DISPLAY_TEXT = (Hash)0xCD015E5BB0D96A57UL;

    static readonly string IniPath = "scripts\\ExhaustPops.ini";
    static readonly string SoundRoot = "scripts\\ExhaustPops\\Sounds";
    static readonly string[] FxNames = { "veh_backfire", "veh_nitrous", "exp_grd_flare" };
    static readonly string[] BoneNames = { "exhaust", "exhaust_2", "exhaust_3", "exhaust_4", "exhaust_5", "exhaust_6", "exhaust_7", "exhaust_8" };
    static readonly int[] DisabledControls = { 172, 173, 174, 175, 176, 177, 24, 25, 22, 23, 51, 52, 85, 99, 100 };

    // ---- bang mau ----
    // che do 0 = mac dinh (khong nhuom), 1-7 = mau goc, 8 = cau vong, 9 = ngau nhien,
    // 10..(10+ExtraCount-1) = mau moi, mau cuoi cung = tuy chinh (Custom RGB)
    static readonly float[][] Palette =
    {
        new float[] { 0.10f, 0.40f, 1.00f },   // 1 xanh duong
        new float[] { 0.70f, 0.10f, 1.00f },   // 2 tim
        new float[] { 0.10f, 1.00f, 0.25f },   // 3 xanh la
        new float[] { 1.00f, 0.05f, 0.05f },   // 4 do
        new float[] { 1.00f, 0.25f, 0.70f },   // 5 hong
        new float[] { 0.00f, 1.00f, 1.00f },   // 6 xanh ngoc
        new float[] { 1.00f, 1.00f, 1.00f }    // 7 trang
    };
    static readonly string[] BaseNamesVi = { "Mặc định (cam)", "Xanh dương", "Tím", "Xanh lá", "Đỏ", "Hồng", "Xanh ngọc", "Trắng", "Cầu vồng", "Ngẫu nhiên" };
    static readonly string[] BaseNamesEn = { "Default (orange)", "Blue", "Purple", "Green", "Red", "Pink", "Cyan", "White", "Rainbow", "Random" };

    static readonly float[][] ExtraRgb =
    {
        new float[] { 1.00f, 0.50f, 0.00f },
        new float[] { 1.00f, 0.90f, 0.10f },
        new float[] { 1.00f, 0.75f, 0.10f },
        new float[] { 1.00f, 0.25f, 0.00f },
        new float[] { 0.80f, 0.00f, 0.10f },
        new float[] { 1.00f, 0.00f, 0.60f },
        new float[] { 1.00f, 0.60f, 0.80f },
        new float[] { 0.45f, 0.00f, 0.90f },
        new float[] { 0.70f, 0.55f, 1.00f },
        new float[] { 0.00f, 0.15f, 0.90f },
        new float[] { 0.00f, 0.50f, 1.00f },
        new float[] { 0.35f, 0.75f, 1.00f },
        new float[] { 0.70f, 0.95f, 1.00f },
        new float[] { 0.00f, 0.70f, 0.60f },
        new float[] { 0.40f, 1.00f, 0.60f },
        new float[] { 0.20f, 1.00f, 0.20f },
        new float[] { 0.65f, 1.00f, 0.10f },
        new float[] { 1.00f, 0.90f, 0.75f },
        new float[] { 0.50f, 0.25f, 0.05f }
    };
    static readonly string[] ExtraNamesVi =
    {
        "Cam", "Vàng", "Vàng kim", "Cam đỏ", "Đỏ đậm", "Hồng đậm", "Hồng nhạt", "Tím đậm", "Tím nhạt",
        "Xanh dương đậm", "Xanh điện", "Xanh da trời", "Xanh băng", "Xanh mòng két", "Xanh bạc hà",
        "Xanh neon", "Xanh chanh", "Trắng ấm", "Nâu"
    };
    static readonly string[] ExtraNamesEn =
    {
        "Orange", "Yellow", "Gold", "Red-orange", "Crimson", "Magenta", "Light pink", "Violet", "Lavender",
        "Deep blue", "Electric blue", "Sky blue", "Ice", "Teal", "Mint",
        "Neon green", "Lime", "Warm white", "Brown"
    };
    const int ExtraStart = 10;
    static int ExtraCount { get { return ExtraRgb.Length; } }
    static int CustomMode { get { return ExtraStart + ExtraRgb.Length; } }
    static int ModeCount { get { return ExtraStart + ExtraRgb.Length + 1; } }

    const int MenuItems = 26;   // so muc trong menu

    // ---- cai dat ----
    bool enabled = true;
    float minSpeed = 0f, inputThreshold = 0.5f;
    int minIntervalMs = 60, maxIntervalMs = 130;
    int triggerMode = 2;
    float liftMinSpeed = 8f, liftMinRpm = 0.55f;
    int liftDurationMs = 1200;
    bool shiftPops = true, autoPops = true, cruisePops = true;
    int cruiseStyle = 0, volleyMode = 3;
    float flameScale = 2f;
    int colorMode = 0;
    int customR = 255, customG = 120, customB = 20;
    bool bigFlame = false;
    int flameFx = 0;     // 0 veh_backfire, 1 veh_nitrous, 2 exp_grd_flare, 3 Tu dong
    bool glow = false;
    int flameBursts = 3;
    float rotZ = 0f;
    bool playSound = true, playSub = true;
    float gain = 3f;
    string soundSet = "Default";
    int soundIndex = -1;
    Keys openKey = Keys.F7, toggleKey = Keys.F8;
    bool vi = true;

    // ---- trang thai ----
    readonly Random rng = new Random();
    readonly List<SoundPlayer> pops = new List<SoundPlayer>();
    readonly List<string> popNames = new List<string>();
    readonly List<string> setNames = new List<string>();
    readonly List<Vector3> glowOffs = new List<Vector3>();
    SoundPlayer sub;
    int ampSkipped;
    bool audioErrorShown;
    bool fxLoaded;
    bool menuOpen;
    int sel;
    string status = "";
    int statusUntil;
    int lastVehHandle;
    float lastGas, lastRpm;
    int lastGear;
    int liftUntil, shiftPopsLeft, volleyLeft, nextVolleyTime, nextPopTime, tripIndex = -1, cruiseBurst;
    int glowHandle, glowUntil;
    float glowR = 1f, glowG = 0.5f, glowB = 0.1f;

    public ExhaustPops()
    {
        Tick += OnTick;
        KeyDown += OnKeyDown;
        LoadConfig();
        ScanSets();
        LoadSet();
    }

    // =====================================================================
    //  INI
    // =====================================================================
    static bool PBool(string v, bool d)
    {
        string s = v.Trim().ToLowerInvariant();
        if (s == "true" || s == "1" || s == "yes" || s == "on") return true;
        if (s == "false" || s == "0" || s == "no" || s == "off") return false;
        return d;
    }
    static float PFloat(string v, float d)
    {
        float f;
        return float.TryParse(v.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out f) ? f : d;
    }
    static int PInt(string v, int d)
    {
        int n;
        return int.TryParse(v.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n) ? n : d;
    }
    static float Clamp(float v, float lo, float hi) { return v < lo ? lo : (v > hi ? hi : v); }
    static int ClampI(int v, int lo, int hi) { return v < lo ? lo : (v > hi ? hi : v); }

    void LoadConfig()
    {
        try
        {
            if (!File.Exists(IniPath)) return;
            Dictionary<string, string> kv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string section = "";
            foreach (string raw in File.ReadAllLines(IniPath))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == ';' || line[0] == '#') continue;
                if (line[0] == '[' && line[line.Length - 1] == ']') { section = line.Substring(1, line.Length - 2).Trim(); continue; }
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                kv[section + "." + line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }
            Func<string, string, string> g = delegate (string key, string def) { string val; return kv.TryGetValue(key, out val) ? val : def; };

            enabled = PBool(g("Main.Enabled", "true"), true);
            minSpeed = Clamp(PFloat(g("Main.MinSpeed", "0"), 0f), 0f, 60f);
            inputThreshold = Clamp(PFloat(g("Main.InputThreshold", "0.5"), 0.5f), 0.1f, 1f);
            minIntervalMs = ClampI(PInt(g("Main.MinIntervalMs", "60"), 60), 30, 1000);
            maxIntervalMs = ClampI(PInt(g("Main.MaxIntervalMs", "130"), 130), 30, 1500);
            if (maxIntervalMs < minIntervalMs) maxIntervalMs = minIntervalMs;
            triggerMode = ClampI(PInt(g("Main.TriggerMode", "2"), 2), 0, 2);
            liftMinSpeed = PFloat(g("Main.LiftMinSpeed", "8"), 8f);
            liftDurationMs = PInt(g("Main.LiftDurationMs", "1200"), 1200);
            liftMinRpm = PFloat(g("Main.LiftMinRpm", "0.55"), 0.55f);
            shiftPops = PBool(g("Main.ShiftPops", "true"), true);
            autoPops = PBool(g("Main.AutoPops", "true"), true);
            cruisePops = PBool(g("Main.CruisePops", "true"), true);
            cruiseStyle = ClampI(PInt(g("Main.CruiseStyle", "0"), 0), 0, 4);
            volleyMode = ClampI(PInt(g("Main.VolleyMode", "3"), 3), 0, 3);

            flameScale = Clamp(PFloat(g("Effect.FlameScale", "2.0"), 2f), 0.25f, 15f);
            colorMode = ClampI(PInt(g("Effect.ColorMode", "0"), 0), 0, ModeCount - 1);
            customR = ClampI(PInt(g("Effect.CustomR", "255"), 255), 0, 255);
            customG = ClampI(PInt(g("Effect.CustomG", "120"), 120), 0, 255);
            customB = ClampI(PInt(g("Effect.CustomB", "20"), 20), 0, 255);
            bigFlame = PBool(g("Effect.BigFlame", "false"), false);
            flameFx = ClampI(PInt(g("Effect.FlameFx", "0"), 0), 0, 3);
            glow = PBool(g("Effect.Glow", "false"), false);
            flameBursts = ClampI(PInt(g("Effect.FlameBursts", "3"), 3), 1, 10);
            rotZ = PFloat(g("Effect.RotZ", "0"), 0f);

            playSound = PBool(g("Sound.Enabled", "true"), true);
            playSub = PBool(g("Sound.PlaySub", "true"), true);
            gain = Clamp(PFloat(g("Sound.Gain", "3.0"), 3f), 0.25f, 20f);
            soundSet = g("Sound.Set", "Default");
            soundIndex = PInt(g("Sound.PopIndex", "-1"), -1);

            Keys k;
            if (Enum.TryParse<Keys>(g("Menu.OpenKey", "F7"), true, out k)) openKey = k;
            if (Enum.TryParse<Keys>(g("Menu.ToggleKey", "F8"), true, out k)) toggleKey = k;
            vi = !string.Equals(g("Menu.Language", "vi").Trim(), "en", StringComparison.OrdinalIgnoreCase);
        }
        catch { }
    }

    static string B(bool v) { return v ? "true" : "false"; }
    static string F(float v) { return v.ToString("0.###", CultureInfo.InvariantCulture); }

    void SaveConfig()
    {
        try
        {
            StringBuilder s = new StringBuilder();
            s.AppendLine("[Main]");
            s.AppendLine("Enabled=" + B(enabled));
            s.AppendLine("MinSpeed=" + F(minSpeed));
            s.AppendLine("InputThreshold=" + F(inputThreshold));
            s.AppendLine("MinIntervalMs=" + minIntervalMs);
            s.AppendLine("MaxIntervalMs=" + maxIntervalMs);
            s.AppendLine("; 0 = hold throttle+brake, 1 = lift throttle at speed (anti-lag), 2 = both");
            s.AppendLine("TriggerMode=" + triggerMode);
            s.AppendLine("LiftMinSpeed=" + F(liftMinSpeed));
            s.AppendLine("LiftDurationMs=" + liftDurationMs);
            s.AppendLine("LiftMinRpm=" + F(liftMinRpm));
            s.AppendLine("; pop 2-3 times on every upshift (on throttle, high RPM)");
            s.AppendLine("ShiftPops=" + B(shiftPops));
            s.AppendLine("; occasional pops while coasting off-throttle at speed and high RPM");
            s.AppendLine("AutoPops=" + B(autoPops));
            s.AppendLine("; pop continuously the whole time the vehicle is moving");
            s.AppendLine("CruisePops=" + B(cruisePops));
            s.AppendLine("; cruise rhythm: 0 rapid, 1 steady slow beat, 2 bursts of 3 then pause, 3 crackle, 4 follows throttle");
            s.AppendLine("CruiseStyle=" + cruiseStyle);
            s.AppendLine("; volley pops while moving: 0 off, 1 every 3-4 s, 2 every 6-7 s, 3 every 3-7 s (3-6 pops each)");
            s.AppendLine("VolleyMode=" + volleyMode);
            s.AppendLine();
            s.AppendLine("[Effect]");
            s.AppendLine("FlameScale=" + F(flameScale));
            s.AppendLine("; 0 default orange, 1 blue, 2 purple, 3 green, 4 red, 5 pink, 6 cyan, 7 white, 8 rainbow, 9 random,");
            s.AppendLine("; 10-28 more colours (orange, yellow, gold, red-orange, crimson, magenta, light pink, violet, lavender,");
            s.AppendLine("; deep blue, electric blue, sky blue, ice, teal, mint, neon green, lime, warm white, brown),");
            s.AppendLine("; 29 = your own colour from CustomR / CustomG / CustomB below");
            s.AppendLine("ColorMode=" + colorMode);
            s.AppendLine("; custom colour (0-255 each), used when ColorMode=29");
            s.AppendLine("CustomR=" + customR);
            s.AppendLine("CustomG=" + customG);
            s.AppendLine("CustomB=" + customB);
            s.AppendLine("; big flame (flame size x1.5)");
            s.AppendLine("BigFlame=" + B(bigFlame));
            s.AppendLine("; flame effect: 0 veh_backfire (ignores colour tint), 1 veh_nitrous, 2 exp_grd_flare, 3 auto (veh_backfire when colour is default, veh_nitrous when a colour is chosen)");
            s.AppendLine("FlameFx=" + flameFx);
            s.AppendLine("; coloured light at exhaust (made the screen flicker, keep false)");
            s.AppendLine("Glow=" + B(glow));
            s.AppendLine("; number of flame particles per exhaust per pop (1-6)");
            s.AppendLine("FlameBursts=" + flameBursts);
            s.AppendLine("; Set to 180 if flames point the wrong way");
            s.AppendLine("RotZ=" + F(rotZ));
            s.AppendLine();
            s.AppendLine("[Sound]");
            s.AppendLine("Enabled=" + B(playSound));
            s.AppendLine("PlaySub=" + B(playSub));
            s.AppendLine("; Loudness multiplier for the pop sounds (1 = original, 2 = louder, up to 20)");
            s.AppendLine("Gain=" + F(gain));
            s.AppendLine("; Name of a folder inside scripts\\ExhaustPops\\Sounds");
            s.AppendLine("Set=" + soundSet);
            s.AppendLine("; -2 = random sound picked each time you get in a vehicle, -1 = random pop sound, 0 = first sound (EX_POP_0), 1 = second ... (list is sorted by number)");
            s.AppendLine("PopIndex=" + soundIndex);
            s.AppendLine();
            s.AppendLine("[Menu]");
            s.AppendLine("; Key to open/close the menu (System.Windows.Forms.Keys name, e.g. F7, F8, Insert)");
            s.AppendLine("OpenKey=" + openKey);
            s.AppendLine("; Quick on/off key (shows a subtitle)");
            s.AppendLine("ToggleKey=" + toggleKey);
            s.AppendLine("; Menu language: vi (Tieng Viet) or en (English). Use en if accents show as broken characters in game.");
            s.AppendLine("Language=" + (vi ? "vi" : "en"));
            File.WriteAllText(IniPath, s.ToString(), new UTF8Encoding(false));
            Say(vi ? "Đã lưu vào ExhaustPops.ini" : "Saved to ExhaustPops.ini");
        }
        catch (Exception ex)
        {
            Say((vi ? "Không lưu được: " : "Save failed: ") + ex.Message);
        }
    }

    void Say(string text)
    {
        status = text;
        statusUntil = Game.GameTime + 2500;
    }

    // =====================================================================
    //  AM THANH
    // =====================================================================
    static int NumOf(string s)
    {
        int i = s.LastIndexOf('_');
        int n;
        if (i >= 0 && int.TryParse(s.Substring(i + 1), out n)) return n;
        return int.MaxValue;
    }

    void ScanSets()
    {
        setNames.Clear();
        try
        {
            if (Directory.Exists(SoundRoot))
                foreach (string dir in Directory.GetDirectories(SoundRoot)) setNames.Add(Path.GetFileName(dir));
        }
        catch { }
        if (setNames.Count > 0 && !setNames.Contains(soundSet)) soundSet = setNames[0];
    }

    // khuech dai file WAV 16-bit (giong ban goc: nhan he so, gain > 1 thi nen mem bang tanh)
    byte[] Amplify(byte[] wav, float g)
    {
        if (g <= 1.001f && g >= 0.999f) return wav;
        try
        {
            byte[] w = (byte[])wav.Clone();
            int pos = 12;
            int bits = 16;
            int fmt = 1;
            while (pos + 8 <= w.Length)
            {
                string id = Encoding.ASCII.GetString(w, pos, 4);
                int size = BitConverter.ToInt32(w, pos + 4);
                if (id == "fmt " && pos + 24 <= w.Length)
                {
                    fmt = BitConverter.ToUInt16(w, pos + 8);
                    bits = BitConverter.ToUInt16(w, pos + 22);
                }
                else if (id == "data")
                {
                    if (bits != 16 || (fmt != 1 && fmt != 65534))
                    {
                        ampSkipped++;
                        return wav;
                    }
                    int start = pos + 8;
                    int end = (size > 0 && start + size <= w.Length) ? start + size : w.Length;
                    for (int i = start; i + 1 < end; i += 2)
                    {
                        double x = BitConverter.ToInt16(w, i) / 32768.0 * g;
                        double y = g > 1f ? Math.Tanh(x) : x;
                        short s = (short)Math.Max(-32768.0, Math.Min(32767.0, y * 32767.0));
                        w[i] = (byte)(s & 255);
                        w[i + 1] = (byte)((s >> 8) & 255);
                    }
                    break;
                }
                else if (size < 0)
                {
                    break;
                }
                pos += 8 + size + (size & 1);
            }
            return w;
        }
        catch { return wav; }
    }

    void LoadSet()
    {
        pops.Clear();
        popNames.Clear();
        sub = null;
        ampSkipped = 0;
        string folder = Path.Combine(SoundRoot, soundSet);
        if (!Directory.Exists(folder)) return;
        try
        {
            string[] files = Directory.GetFiles(folder, "*.wav");
            Array.Sort(files, delegate (string a, string b2)
            {
                string na = Path.GetFileNameWithoutExtension(a).ToUpperInvariant();
                string nb = Path.GetFileNameWithoutExtension(b2).ToUpperInvariant();
                int c = NumOf(na).CompareTo(NumOf(nb));
                if (c == 0) return string.CompareOrdinal(na, nb);
                return c;
            });
            foreach (string f in files)
            {
                string name = Path.GetFileNameWithoutExtension(f).ToUpperInvariant();
                if (!name.StartsWith("EX_POP_")) continue;
                try
                {
                    SoundPlayer p = new SoundPlayer(new MemoryStream(Amplify(File.ReadAllBytes(f), gain)));
                    p.Load();
                    if (name == "EX_POP_SUB") sub = p;
                    else { pops.Add(p); popNames.Add(name); }
                }
                catch { }
            }
        }
        catch { }
        if (soundIndex >= pops.Count) soundIndex = -1;
    }

    void Pop()
    {
        if (!playSound) return;
        try
        {
            if (pops.Count > 0)
            {
                int idx;
                if (soundIndex >= 0 && soundIndex < pops.Count) idx = soundIndex;
                else if (soundIndex == -2 && tripIndex >= 0 && tripIndex < pops.Count) idx = tripIndex;
                else idx = rng.Next(pops.Count);
                pops[idx].Play();
            }
            if (playSub && sub != null) sub.Play();
        }
        catch
        {
            if (!audioErrorShown)
            {
                audioErrorShown = true;
                GTA.UI.Screen.ShowSubtitle(vi ? "ExhaustPops: không phát được âm thanh (chỉ có lửa)" : "ExhaustPops: audio playback failed (flames only)", 5000);
            }
        }
    }

    // =====================================================================
    //  MAU
    // =====================================================================
    static void Rainbow(double phase, out float r, out float g, out float b)
    {
        r = (float)(0.5 + 0.5 * Math.Sin(phase));
        g = (float)(0.5 + 0.5 * Math.Sin(phase + 2.094));
        b = (float)(0.5 + 0.5 * Math.Sin(phase + 4.188));
    }

    // mau cua mot che do (de nhuom lua va de ve o mau xem truoc)
    void ModeColor(int mode, double phase, out float r, out float g, out float b)
    {
        if (mode >= 1 && mode <= 7)
        {
            float[] c = Palette[mode - 1];
            r = c[0]; g = c[1]; b = c[2];
        }
        else if (mode == 8)
        {
            Rainbow(phase, out r, out g, out b);
        }
        else if (mode == 9)
        {
            // ngau nhien: chon trong toan bo mau (goc + moi)
            int n = Palette.Length + ExtraRgb.Length;
            int i = rng.Next(n);
            float[] c = i < Palette.Length ? Palette[i] : ExtraRgb[i - Palette.Length];
            r = c[0]; g = c[1]; b = c[2];
        }
        else if (mode >= ExtraStart && mode < ExtraStart + ExtraRgb.Length)
        {
            float[] c = ExtraRgb[mode - ExtraStart];
            r = c[0]; g = c[1]; b = c[2];
        }
        else if (mode == CustomMode)
        {
            r = customR / 255f; g = customG / 255f; b = customB / 255f;
        }
        else
        {
            r = 1f; g = 0.5f; b = 0.1f;   // mac dinh (chi de hien thi)
        }
    }

    string ModeName(int mode)
    {
        if (mode >= 0 && mode < ExtraStart) return vi ? BaseNamesVi[mode] : BaseNamesEn[mode];
        if (mode >= ExtraStart && mode < ExtraStart + ExtraRgb.Length)
            return vi ? ExtraNamesVi[mode - ExtraStart] : ExtraNamesEn[mode - ExtraStart];
        if (mode == CustomMode) return vi ? "Tùy chỉnh (RGB)" : "Custom (RGB)";
        return "?";
    }

    // =====================================================================
    //  LUA
    // =====================================================================
    void Flames(Vehicle v)
    {
        if (!fxLoaded)
        {
            Function.Call(H_REQUEST_NAMED_PTFX_ASSET, "core");
            if (!Function.Call<bool>(H_HAS_NAMED_PTFX_ASSET_LOADED, "core")) return;
            fxLoaded = true;
        }
        glowOffs.Clear();
        bool found = false;
        foreach (string bone in BoneNames)
        {
            int idx = Function.Call<int>(H_GET_ENTITY_BONE_INDEX_BY_NAME, v.Handle, bone);
            if (idx == -1) continue;
            found = true;
            Vector3 world = Function.Call<Vector3>(H_GET_WORLD_POSITION_OF_ENTITY_BONE, v.Handle, idx);
            Vector3 local = v.GetPositionOffset(world);
            Burst(v, local);
        }
        if (!found)
        {
            try
            {
                OutputArgument mn = new OutputArgument();
                OutputArgument mx = new OutputArgument();
                Function.Call(H_GET_MODEL_DIMENSIONS, v.Model.Hash, mn, mx);
                Vector3 min = mn.GetResult<Vector3>();
                Burst(v, new Vector3(0f, min.Y, min.Z + 0.35f));
            }
            catch { }
        }
    }

    void Burst(Vehicle v, Vector3 off)
    {
        float scale = bigFlame ? flameScale * 1.5f : flameScale;
        glowOffs.Add(off);
        glowHandle = v.Handle;
        glowUntil = Game.GameTime + 100;

        // hieu ung thuc su dung: che do Tu dong -> co mau thi dung veh_nitrous (an mau)
        int fx = flameFx;
        if (fx == 3) fx = colorMode > 0 ? 1 : 0;

        for (int i = 0; i < flameBursts; i++)
        {
            Function.Call(H_USE_PARTICLE_FX_ASSET, "core");
            if (colorMode > 0)
            {
                float r, g, b;
                double phase = Game.GameTime / 400.0 + i * 0.3;
                ModeColor(colorMode, phase, out r, out g, out b);
                glowR = r; glowG = g; glowB = b;
                Function.Call(H_SET_PARTICLE_FX_NON_LOOPED_COLOUR, r, g, b);
            }
            Function.Call(H_START_PARTICLE_FX_NON_LOOPED_ON_ENTITY, FxNames[fx], v.Handle,
                          off.X, off.Y, off.Z, 0f, 0f, rotZ, scale, false, false, false);
        }
    }

    void DrawGlow(Vehicle v)
    {
        if (!glow || Game.GameTime >= glowUntil || glowHandle != v.Handle) return;
        foreach (Vector3 o in glowOffs)
        {
            Vector3 p = v.GetOffsetPosition(o);
            float fade = Math.Max(0f, (glowUntil - Game.GameTime) / 100f);
            Function.Call(H_DRAW_LIGHT_WITH_RANGE, p.X, p.Y, p.Z,
                          (int)(glowR * 255f), (int)(glowG * 255f), (int)(glowB * 255f), 1.6f, 0.9f * fade);
        }
    }

    // =====================================================================
    //  THOI GIAN NO PO
    // =====================================================================
    int NextVolleyDelay()
    {
        switch (volleyMode)
        {
            case 1: return rng.Next(3000, 4001);
            case 2: return rng.Next(6000, 7001);
            default: return rng.Next(3000, 7001);
        }
    }

    int CruiseGap(float gas)
    {
        int lo = minIntervalMs;
        int hi = Math.Max(maxIntervalMs * 3, 250);
        switch (cruiseStyle)
        {
            case 1: return hi;
            case 2:
                cruiseBurst = (cruiseBurst + 1) % 3;
                if (cruiseBurst != 0) return lo;
                return rng.Next(450, 750);
            case 3:
                if (rng.Next(100) < 30) return rng.Next(300, 700);
                return rng.Next(lo, maxIntervalMs * 2 + 1);
            case 4:
                return (int)(hi - (hi - lo) * Clamp(gas, 0f, 1f));
            default:
                return rng.Next(lo, lo + 31);
        }
    }

    bool JustPressed(int control)
    {
        return Function.Call<bool>(H_IS_CONTROL_JUST_PRESSED, 0, control);
    }

    // =====================================================================
    //  VONG LAP CHINH
    // =====================================================================
    void OnTick(object sender, EventArgs e)
    {
        HandleMenu();
        if (menuOpen) DrawMenu();
        if (!enabled) return;

        Ped p = Game.Player.Character;
        if (p == null || !p.Exists() || !p.IsInVehicle())
        {
            lastVehHandle = 0;
            return;
        }
        Vehicle v = p.CurrentVehicle;
        if (v == null || !v.Exists() || v.Driver != p || !v.IsEngineRunning) return;

        if (v.Handle != lastVehHandle)
        {
            lastVehHandle = v.Handle;
            lastGas = 0f; lastGear = 0; lastRpm = 0f;
            liftUntil = 0; shiftPopsLeft = 0; volleyLeft = 0;
            nextVolleyTime = Game.GameTime + NextVolleyDelay();
            tripIndex = pops.Count > 0 ? rng.Next(pops.Count) : -1;
        }

        DrawGlow(v);

        float gas = Function.Call<float>(H_GET_CONTROL_NORMAL, 0, 71);
        float brake = Function.Call<float>(H_GET_CONTROL_NORMAL, 0, 72);
        int now = Game.GameTime;

        if (lastGas >= 0.6f && gas < 0.3f && v.Speed >= liftMinSpeed && v.CurrentRPM >= liftMinRpm)
            liftUntil = now + liftDurationMs;
        lastGas = gas;

        int gear = v.CurrentGear;
        if (shiftPops && gear > lastGear && lastGear > 0 && gas >= 0.6f && lastRpm >= liftMinRpm && v.Speed >= liftMinSpeed)
            shiftPopsLeft = rng.Next(2, 4);
        lastGear = gear;
        lastRpm = v.CurrentRPM;

        bool a = shiftPopsLeft > 0;
        if ((triggerMode == 0 || triggerMode == 2) && gas >= inputThreshold && brake >= inputThreshold && v.Speed >= minSpeed)
            a = true;
        if ((triggerMode == 1 || triggerMode == 2) && now < liftUntil && gas < 0.3f && v.Speed >= liftMinSpeed)
            a = true;

        bool b = autoPops && gas < 0.3f && v.Speed >= liftMinSpeed && v.CurrentRPM >= liftMinRpm;
        bool c = cruisePops && v.Speed >= Math.Max(minSpeed, 1f);
        bool d = false;
        if (volleyMode > 0 && v.Speed >= Math.Max(minSpeed, 1f))
        {
            if (volleyLeft > 0) d = true;
            else if (now >= nextVolleyTime)
            {
                volleyLeft = rng.Next(3, 7);
                nextVolleyTime = now + NextVolleyDelay();
                d = true;
            }
        }

        if (!a && !b && !c && !d) return;
        if (now < nextPopTime) return;

        int gap = rng.Next(minIntervalMs, maxIntervalMs + 1);
        if (c) gap = CruiseGap(gas);
        else if (d) gap = rng.Next(minIntervalMs, maxIntervalMs + 1);

        nextPopTime = (a || c || d) ? now + gap : now + rng.Next(minIntervalMs * 3, maxIntervalMs * 5 + 1);

        if (shiftPopsLeft > 0) shiftPopsLeft--;
        if (volleyLeft > 0) volleyLeft--;

        Flames(v);
        Pop();
    }

    // =====================================================================
    //  MENU
    // =====================================================================
    void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.KeyCode == openKey)
        {
            menuOpen = !menuOpen;
            if (menuOpen) ScanSets();
            return;
        }
        if (e.KeyCode == toggleKey)
        {
            enabled = !enabled;
            GTA.UI.Screen.ShowSubtitle(vi ? (enabled ? "Nổ pô: BẬT" : "Nổ pô: TẮT") : (enabled ? "Exhaust pops: ON" : "Exhaust pops: OFF"), 1500);
            return;
        }
        if (!menuOpen) return;
        switch (e.KeyCode)
        {
            case Keys.Back:
            case Keys.Escape: menuOpen = false; break;
            case Keys.Enter: Change(1); break;
            case Keys.Left: Change(-1); break;
            case Keys.Up: sel = (sel + MenuItems - 1) % MenuItems; break;
            case Keys.Right: Change(1); break;
            case Keys.Down: sel = (sel + 1) % MenuItems; break;
        }
    }

    void HandleMenu()
    {
        if (!menuOpen) return;
        foreach (int ctl in DisabledControls)
            Function.Call(H_DISABLE_CONTROL_ACTION, 0, ctl, true);

        if (JustPressed(173)) { sel = (sel + 1) % MenuItems; return; }
        if (JustPressed(172)) { sel = (sel + MenuItems - 1) % MenuItems; return; }
        if (JustPressed(174)) { Change(-1); return; }
        if (JustPressed(175) || JustPressed(176)) { Change(1); return; }
        if (JustPressed(177)) menuOpen = false;
    }

    // thu tu cac muc menu:
    //  0 no po | 1 am thanh | 2 am phu | 3 bo am thanh | 4 nguong ga/phanh | 5 toc do toi thieu | 6 khoang min | 7 khoang max
    //  8 co lua | 9 che do kich hoat | 10 so tia lua | 11 am luong | 12 no khi len so | 13 MAU LUA | 14 Custom R | 15 Custom G
    //  16 Custom B | 17 no tu dong | 18 ngon ngu | 19 lua lon | 20 no khi chay | 21 am thanh no | 22 nhip khi chay
    //  23 hieu ung lua | 24 loat no | 25 luu
    void Change(int dir)
    {
        switch (sel)
        {
            case 0: enabled = !enabled; break;
            case 1: playSound = !playSound; break;
            case 2: playSub = !playSub; break;
            case 3:
                if (setNames.Count > 0)
                {
                    int i = setNames.IndexOf(soundSet);
                    i = (i + dir + setNames.Count) % setNames.Count;
                    soundSet = setNames[i];
                    LoadSet();
                    Pop();
                }
                break;
            case 4: inputThreshold = Clamp(inputThreshold + 0.1f * dir, 0.1f, 1f); break;
            case 5: minSpeed = Clamp(minSpeed + 2f * dir, 0f, 60f); break;
            case 6:
                minIntervalMs = (int)Clamp(minIntervalMs + 20 * dir, 30f, 1000f);
                if (maxIntervalMs < minIntervalMs) maxIntervalMs = minIntervalMs;
                break;
            case 7:
                maxIntervalMs = (int)Clamp(maxIntervalMs + 20 * dir, 30f, 1500f);
                if (minIntervalMs > maxIntervalMs) minIntervalMs = maxIntervalMs;
                break;
            case 8: flameScale = Clamp(flameScale + 0.25f * dir, 0.25f, 15f); break;
            case 9: triggerMode = (triggerMode + dir + 3) % 3; break;
            case 10: flameBursts = (int)Clamp(flameBursts + dir, 1f, 10f); break;
            case 11:
                gain = Clamp(gain + (gain >= 6f ? 1f : 0.25f) * dir, 0.25f, 20f);
                LoadSet();
                if (ampSkipped > 0)
                    Say(ampSkipped + (vi ? " file không phải WAV 16-bit, không tăng được" : " file not 16-bit WAV, cannot amplify"));
                Pop();
                break;
            case 12: shiftPops = !shiftPops; break;
            case 13: colorMode = (colorMode + dir + ModeCount) % ModeCount; break;
            case 14: customR = ClampI(customR + 15 * dir, 0, 255); colorMode = CustomMode; break;
            case 15: customG = ClampI(customG + 15 * dir, 0, 255); colorMode = CustomMode; break;
            case 16: customB = ClampI(customB + 15 * dir, 0, 255); colorMode = CustomMode; break;
            case 17: autoPops = !autoPops; break;
            case 18: vi = !vi; break;
            case 19: bigFlame = !bigFlame; break;
            case 20: cruisePops = !cruisePops; break;
            case 21:
                if (pops.Count > 0)
                {
                    int n = pops.Count + 2;
                    soundIndex = (((soundIndex + 2 + dir) % n) + n) % n - 2;
                    Pop();
                }
                break;
            case 22: cruiseStyle = (cruiseStyle + dir + 5) % 5; break;
            case 23: flameFx = (flameFx + dir + 4) % 4; break;
            case 24: volleyMode = (volleyMode + dir + 4) % 4; break;
            case 25: SaveConfig(); break;
        }
    }

    string OnOff(bool v)
    {
        return vi ? (v ? "BẬT" : "TẮT") : (v ? "ON" : "OFF");
    }

    string Label(int i)
    {
        switch (i)
        {
            case 0: return (vi ? "Nổ pô: " : "Exhaust pops: ") + OnOff(enabled);
            case 1: return (vi ? "Âm thanh: " : "Sound: ") + OnOff(playSound);
            case 2: return (vi ? "Âm phụ (sub): " : "Sub sound: ") + OnOff(playSub);
            case 3: return (vi ? "Bộ âm thanh: " : "Sound set: ") + soundSet + " (" + pops.Count + (sub != null ? "+SUB" : "") + ")";
            case 4: return (vi ? "Ngưỡng ga/phanh: " : "Throttle/brake threshold: ") + F(inputThreshold);
            case 5: return (vi ? "Tốc độ tối thiểu (m/s): " : "Min speed (m/s): ") + F(minSpeed);
            case 6: return (vi ? "Khoảng nổ nhỏ nhất (ms): " : "Min interval (ms): ") + minIntervalMs;
            case 7: return (vi ? "Khoảng nổ lớn nhất (ms): " : "Max interval (ms): ") + maxIntervalMs;
            case 8: return (vi ? "Cỡ lửa: " : "Flame size: ") + flameScale.ToString("0.00", CultureInfo.InvariantCulture);
            case 9:
                {
                    string[] t = vi ? new[] { "Giữ ga+phanh", "Nhả ga", "Cả hai" } : new[] { "Hold throttle+brake", "Lift throttle", "Both" };
                    return (vi ? "Kích hoạt: " : "Trigger: ") + t[triggerMode];
                }
            case 10: return (vi ? "Số tia lửa mỗi lần nổ: " : "Flame bursts per pop: ") + flameBursts;
            case 11: return (vi ? "Âm lượng nổ: x" : "Pop volume: x") + F(gain);
            case 12: return (vi ? "Nổ khi lên số: " : "Upshift pops: ") + OnOff(shiftPops);
            case 13:
                {
                    string s = (vi ? "MÀU LỬA " : "FLAME COLOUR ") + "[" + colorMode + "/" + (ModeCount - 1) + "]: " + ModeName(colorMode);
                    if (colorMode > 0 && flameFx == 0)
                        s += vi ? "  (veh_backfire không ăn màu)" : "  (veh_backfire ignores colour)";
                    return s;
                }
            case 14: return (vi ? "Màu tùy chỉnh - Đỏ (R): " : "Custom colour - Red (R): ") + customR;
            case 15: return (vi ? "Màu tùy chỉnh - Lục (G): " : "Custom colour - Green (G): ") + customG;
            case 16: return (vi ? "Màu tùy chỉnh - Lam (B): " : "Custom colour - Blue (B): ") + customB;
            case 17: return (vi ? "Nổ tự động khi lái: " : "Auto pops while driving: ") + OnOff(autoPops);
            case 18: return (vi ? "Ngôn ngữ: Tiếng Việt (đổi sang English)" : "Language: English (switch to Tiếng Việt)");
            case 19: return (vi ? "Lửa lớn: " : "Big flame: ") + OnOff(bigFlame);
            case 20: return (vi ? "Nổ liên tục khi chạy: " : "Cruise pops (always): ") + OnOff(cruisePops);
            case 21:
                {
                    string n;
                    if (pops.Count == 0) n = "-";
                    else if (soundIndex == -2) n = vi ? "Ngẫu nhiên mỗi lần lên xe" : "Random each drive";
                    else if (soundIndex == -1) n = vi ? "Ngẫu nhiên" : "Random";
                    else n = popNames[soundIndex] + " (" + (soundIndex + 1) + "/" + pops.Count + ")";
                    return (vi ? "Âm nổ: " : "Pop sound: ") + n;
                }
            case 22:
                {
                    string[] t = vi ? new[] { "Nhanh", "Đều chậm", "Chùm 3 rồi nghỉ", "Lách tách", "Theo ga" }
                                    : new[] { "Rapid", "Steady slow", "Bursts of 3", "Crackle", "Follows throttle" };
                    return (vi ? "Nhịp khi chạy: " : "Cruise rhythm: ") + t[cruiseStyle];
                }
            case 23:
                {
                    string[] t = vi ? new[] { "veh_backfire", "veh_nitrous", "exp_grd_flare", "Tự động (có màu thì dùng veh_nitrous)" }
                                    : new[] { "veh_backfire", "veh_nitrous", "exp_grd_flare", "Auto (veh_nitrous when coloured)" };
                    return (vi ? "Hiệu ứng lửa: " : "Flame effect: ") + t[flameFx];
                }
            case 24:
                {
                    string[] t = vi ? new[] { "Tắt", "Mỗi 3-4 giây", "Mỗi 6-7 giây", "Mỗi 3-7 giây" }
                                    : new[] { "Off", "Every 3-4 s", "Every 6-7 s", "Every 3-7 s" };
                    return (vi ? "Loạt nổ: " : "Volley pops: ") + t[volleyMode];
                }
            case 25: return vi ? "[ Lưu vào file ini ]" : "[ Save to ini ]";
        }
        return "";
    }

    static void Rect(float cx, float cy, float w, float h, int r, int g, int b, int a)
    {
        Function.Call(H_DRAW_RECT, cx, cy, w, h, r, g, b, a);
    }

    static void Text(string text, float x, float y, float scale, int r, int g, int b)
    {
        Function.Call(H_SET_TEXT_FONT, 4);
        Function.Call(H_SET_TEXT_SCALE, scale, scale);
        Function.Call(H_SET_TEXT_COLOUR, r, g, b, 255);
        Function.Call(H_SET_TEXT_OUTLINE);
        Function.Call(H_BEGIN_TEXT_COMMAND_DISPLAY_TEXT, "STRING");
        Function.Call(H_ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, text);
        Function.Call(H_END_TEXT_COMMAND_DISPLAY_TEXT, x, y, 0);
    }

    void DrawMenu()
    {
        float x0 = 0.22f, w = 0.40f, top = 0.045f, rowH = 0.0315f;
        float left = x0 - w / 2f;

        // nen
        Rect(x0, top + (MenuItems + 1) * rowH / 2f - rowH / 2f, w, (MenuItems + 2) * rowH, 0, 0, 0, 175);
        Text((vi ? "NỔ PÔ" : "EXHAUST POPS") + "  -  Dyza16", left + 0.01f, top - 0.012f, 0.5f, 255, 255, 255);

        for (int i = 0; i < MenuItems; i++)
        {
            float y = top + (i + 1) * rowH;
            if (i == sel)
                Rect(x0, y + 0.0135f, w - 0.01f, rowH - 0.003f, 255, 160, 0, 200);
            Text(Label(i), left + 0.012f, y, 0.34f, 255, 255, 255);

            // o mau xem truoc cho muc mau lua va 3 muc RGB tuy chinh
            if (i >= 13 && i <= 16)
            {
                float r, g, b;
                int mode = i == 13 ? colorMode : CustomMode;
                if (i == 13 && colorMode == 9) { r = 0.8f; g = 0.8f; b = 0.8f; }
                else ModeColor(mode, Game.GameTime / 400.0, out r, out g, out b);
                Rect(x0 + w / 2f - 0.022f, y + 0.0135f, 0.024f, rowH - 0.009f,
                     (int)(r * 255f), (int)(g * 255f), (int)(b * 255f), 255);
            }
        }

        string line;
        if (Game.GameTime < statusUntil) line = status;
        else line = vi ? "Mũi tên/D-pad: chọn, đổi   Enter/A: đổi   Back/B: đóng"
                       : "Arrows/D-pad: move, change   Enter/A: change   Back/B: close";
        Text(line, left + 0.012f, top + (MenuItems + 1) * rowH, 0.30f, 200, 200, 200);
    }
}
