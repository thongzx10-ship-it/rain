using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using GTA;
using GTA.Native;
using GTA.UI;

/// <summary>
/// Rain On Screen - giot mua dong lai tren man hinh (lens), chay bang ScriptHookVDotNet v3.
/// Giot xuat hien theo muc mua cua game, lon dan, du to thi chay xuong va de lai vet giot nho,
/// tanh mua thi kho dan.
/// </summary>
public class RainOnScreen : Script
{
    // ================= CHINH O DAY =================
    // Kich thuoc tinh theo man hinh 1080p, tu dong scale theo do phan giai that.
    const float SpawnPerSecondAtFullRain = 16f;  // so giot moi giay khi mua toi da
    const int   MaxDrops                 = 150;  // gioi han so giot cung luc
    const float MinRadius1080            = 3f;   // ban kinh giot nho nhat (px)
    const float MaxRadius1080            = 20f;  // ban kinh giot lon nhat (px)
    const float GrowthPerSecond1080      = 0.9f; // toc do lon them khi dang mua (px/giay)
    const float SlideRadius1080          = 11f;  // lon hon muc nay thi bat dau chay xuong
    const float LifeMin                  = 9f;   // thoi gian ton tai (giay)
    const float LifeMax                  = 26f;
    const float DryingSpeed              = 3.0f; // tanh mua thi kho nhanh gap may lan
    const float MasterOpacity            = 0.92f;
    const Keys  ToggleKey                = Keys.F10;     // chi dung duoc khi co ban phim
    const bool  ForceRain                = false;        // true = luon mua (de thu khi troi nang)
    // ===============================================

    class Drop
    {
        public float X, Y, R;
        public float Age, Life;
        public bool Sliding;
        public bool Moving;
        public float PhaseTimer;   // thoi gian con lai cua pha dung/chay
        public float Speed;        // px/giay khi dang chay
        public float TrailAcc;     // quang duong da chay tu lan de vet cuoi
        public float Stretch = 1f;
    }

    readonly List<Drop> drops = new List<Drop>();
    readonly Random rng = new Random();
    CustomSprite sprite;
    bool enabled = true;
    float spawnAcc;
    bool loadFailed;

    public RainOnScreen()
    {
        Tick += OnTick;
        KeyUp += OnKeyUp;

        // Tim anh giot nuoc canh file script (khong phu thuoc thu muc lam viec cua game)
        string dir = null;
        try { dir = Path.GetDirectoryName(Filename); } catch { }
        string[] candidates = new string[]
        {
            dir != null ? Path.Combine(Path.Combine(dir, "RainOnScreen"), "droplet.png") : null,
            dir != null ? Path.Combine(dir, "droplet.png") : null,
            Path.Combine(Path.Combine("scripts", "RainOnScreen"), "droplet.png")
        };
        string path = null;
        foreach (string c in candidates)
            if (c != null && File.Exists(c)) { path = c; break; }

        if (path == null)
        {
            loadFailed = true;
            Notification.Show("~r~RainOnScreen: khong tim thay droplet.png (thu muc: " + dir + ")");
            return;
        }
        try
        {
            sprite = new CustomSprite(path, new SizeF(16, 16), new PointF(0, 0));
            Notification.Show("RainOnScreen da nap xong");
        }
        catch (Exception ex)
        {
            loadFailed = true;
            Notification.Show("~r~RainOnScreen loi: " + ex.Message);
        }
    }

    void OnKeyUp(object sender, KeyEventArgs e)
    {
        if (e.KeyCode != ToggleKey) return;
        enabled = !enabled;
        if (!enabled) drops.Clear();
        Notification.Show("Rain On Screen: " + (enabled ? "~g~BAT" : "~r~TAT"));
    }

    float Rand(float a, float b) { return a + (float)rng.NextDouble() * (b - a); }

    void OnTick(object sender, EventArgs e)
    {
        if (loadFailed || !enabled || sprite == null) return;

        float dt = Game.LastFrameTime;
        if (dt <= 0f || dt > 0.25f) dt = 0.016f;

        float W = GTA.UI.Screen.Width, H = GTA.UI.Screen.Height;
        float k = H / 1080f;

        float rain = ForceRain ? 0.7f : Function.Call<float>(Hash.GET_RAIN_LEVEL);
        if (rain < 0f) rain = 0f; if (rain > 1f) rain = 1f;

        Ped ped = Game.Player.Character;
        bool sheltered = ped == null || !ped.Exists()
                         || Function.Call<int>(Hash.GET_INTERIOR_FROM_ENTITY, ped.Handle) != 0;
        bool wet = rain > 0.03f && !sheltered;

        // ---- sinh giot moi ----
        if (wet)
        {
            spawnAcc += rain * SpawnPerSecondAtFullRain * dt;
            while (spawnAcc >= 1f && drops.Count < MaxDrops)
            {
                spawnAcc -= 1f;
                // nhieu giot nho, it giot lon; mua cang to thi cang co nhieu giot lon
                float t = (float)Math.Pow(rng.NextDouble(), 3.2 - 1.6 * rain);
                Drop d = new Drop();
                d.R = (MinRadius1080 + (MaxRadius1080 - MinRadius1080) * 0.55f * t) * k;
                d.X = Rand(0f, W);
                d.Y = Rand(0f, H);
                d.Life = Rand(LifeMin, LifeMax);
                drops.Add(d);
            }
            if (drops.Count >= MaxDrops) spawnAcc = 0f;
        }
        else
        {
            spawnAcc = 0f;
        }

        float ageRate = wet ? 1f : DryingSpeed;
        List<Drop> spawned = null;

        // ---- cap nhat ----
        for (int i = drops.Count - 1; i >= 0; i--)
        {
            Drop d = drops[i];
            d.Age += dt * ageRate;
            if (d.Age >= d.Life) { drops.RemoveAt(i); continue; }

            if (!d.Sliding)
            {
                // dang mua thi giot lon dan (hung them nuoc)
                if (wet && d.R < MaxRadius1080 * k)
                    d.R += GrowthPerSecond1080 * k * rain * dt * Rand(0.4f, 1.2f);

                // du lon thi bat dau chay xuong
                if (d.R > SlideRadius1080 * k && rng.NextDouble() < 0.6 * dt + 0.02 * dt * d.R / k)
                {
                    d.Sliding = true;
                    d.Moving = false;
                    d.PhaseTimer = Rand(0.1f, 0.5f);
                    d.Speed = Rand(60f, 140f) * k * (d.R / (SlideRadius1080 * k));
                }
            }
            else
            {
                d.PhaseTimer -= dt;
                if (d.PhaseTimer <= 0f)
                {
                    d.Moving = !d.Moving;
                    d.PhaseTimer = d.Moving ? Rand(0.3f, 1.1f) : Rand(0.15f, 0.9f);
                    d.Speed = Rand(70f, 170f) * k * Math.Max(0.5f, d.R / (SlideRadius1080 * k));
                }

                if (d.Moving)
                {
                    float dy = d.Speed * dt;
                    d.Y += dy;
                    d.X += (float)Math.Sin(d.Age * 4f + d.Life) * 6f * k * dt;
                    d.TrailAcc += dy;
                    d.R -= 0.35f * k * dt;                       // mat nuoc dan
                    d.Stretch += (1.35f - d.Stretch) * Math.Min(1f, 8f * dt);

                    // de lai vet giot nho phia sau
                    if (d.TrailAcc > d.R * 2.4f)
                    {
                        d.TrailAcc = 0f;
                        if (spawned == null) spawned = new List<Drop>();
                        Drop tr = new Drop();
                        tr.R = Math.Max(1.5f * k, d.R * Rand(0.18f, 0.32f));
                        tr.X = d.X + Rand(-1.5f, 1.5f) * k;
                        tr.Y = d.Y - d.R * 1.2f;
                        tr.Age = 0f;
                        tr.Life = Rand(3f, 8f);
                        spawned.Add(tr);
                    }
                }
                else
                {
                    d.Stretch += (1f - d.Stretch) * Math.Min(1f, 6f * dt);
                }

                if (d.R < 2f * k || d.Y - d.R > H) { drops.RemoveAt(i); continue; }
            }
        }

        if (spawned != null)
            foreach (Drop tr in spawned)
                if (drops.Count < MaxDrops + 60) drops.Add(tr);

        // ---- ve ----
        foreach (Drop d in drops)
        {
            float fadeIn = Math.Min(1f, d.Age / 0.35f);
            float fadeOut = Math.Min(1f, (d.Life - d.Age) / 2.5f);
            float a = Math.Min(fadeIn, fadeOut) * MasterOpacity;
            if (a <= 0.01f) continue;

            float pop = 0.6f + 0.4f * fadeIn;   // moi xuat hien thi "no" ra
            float rx = d.R * pop;
            float ry = d.R * pop * d.Stretch;

            sprite.Size = new SizeF(rx * 2f, ry * 2f);
            sprite.Position = new PointF(d.X - rx, d.Y - ry);
            sprite.Color = Color.FromArgb((int)(a * 255f), 255, 255, 255);
            sprite.Draw();
        }
    }
}
