using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using GooseShared;
using SamEngine;

namespace DefaultMod
{
    public class ModEntryPoint : IMod
    {
        private const int VirtualKeyEscape = 0x1B;
        private const float TeaseRadius = 180f;
        private const float TeaseComboWindowSeconds = 2.2f;
        private const int TeaseClicksNeeded = 3;
        private const float ManualMadDurationSeconds = 9f;

        private readonly Random _random = new Random();
        private readonly HashSet<int> _aggressiveTaskIndexes = new HashSet<int>();
        private readonly string[] _knownAggressiveTasks =
        {
            "AttackMouse",
            "ChargeMouse",
            "CollectWindow",
            "TrackMud",
            "StealMouse",
            "RunToTarget"
        };

        private bool _indexesResolved;
        private bool _virusWallpaperActive;
        private bool _originalWallpaperCaptured;
        private int _teaseComboCount;
        private float _lastTeaseTime;
        private float _manualMadUntilTime;

        private string _virusWallpaperPath;
        private string _originalWallpaper;
        private string _originalWallpaperStyle;
        private string _originalTileWallpaper;

        void IMod.Init()
        {
            InjectionPoints.PostTickEvent += PostTick;
            InjectionPoints.PostRenderEvent += PostRender;
        }

        private void ResolveAggressiveTaskIndexes()
        {
            if (_indexesResolved)
            {
                return;
            }

            _indexesResolved = true;
            foreach (string taskID in _knownAggressiveTasks)
            {
                int index = API.TaskDatabase.getTaskIndexByID(taskID);
                if (index >= 0)
                {
                    _aggressiveTaskIndexes.Add(index);
                }
            }
        }

        public void PostTick(GooseEntity goose)
        {
            ResolveAggressiveTaskIndexes();
            UpdateManualMadState(goose);

            if (IsEscapePressed())
            {
                RestoreWallpaper();
                Environment.Exit(0);
                return;
            }

            bool gooseIsMad = IsGooseMad(goose);
            if (gooseIsMad)
            {
                ApplyVirusWallpaper();
            }
            else
            {
                RestoreWallpaper();
            }
        }

        public void PostRender(GooseEntity goose, Graphics gfx)
        {
            if (!IsGooseMad(goose))
            {
                return;
            }

            RectangleF bounds = gfx.VisibleClipBounds;
            if (_random.NextDouble() < 0.7)
            {
                int alpha = _random.Next(45, 140);
                using (SolidBrush brush = new SolidBrush(Color.FromArgb(alpha, 220, 20, 20)))
                {
                    gfx.FillRectangle(brush, bounds);
                }
            }

            if (_random.NextDouble() < 0.5)
            {
                using (Pen glitchPen = new Pen(Color.FromArgb(180, 255, 255, 255), _random.Next(1, 4)))
                {
                    float y = (float)(_random.NextDouble() * bounds.Height);
                    gfx.DrawLine(glitchPen, 0, y, bounds.Width, y);
                }
            }
        }

        private bool IsGooseMad(GooseEntity goose)
        {
            bool aggressiveTask = _aggressiveTaskIndexes.Contains(goose.currentTask);
            bool movingAggressively = goose.currentSpeed >= goose.parameters.RunSpeed * 0.95f;
            bool highAcceleration = goose.currentAcceleration >= goose.parameters.AccelerationCharged * 0.8f;
            bool manuallyProvoked = Time.time <= _manualMadUntilTime;

            return aggressiveTask || movingAggressively || highAcceleration || manuallyProvoked;
        }

        private void UpdateManualMadState(GooseEntity goose)
        {
            if (!Input.leftMouseButton.Clicked)
            {
                return;
            }

            Vector2 mousePos = new Vector2(Input.mouseX, Input.mouseY);
            float distanceToGoose = Vector2.Magnitude(mousePos - goose.position);
            if (distanceToGoose > TeaseRadius)
            {
                return;
            }

            if (Time.time - _lastTeaseTime > TeaseComboWindowSeconds)
            {
                _teaseComboCount = 0;
            }

            _teaseComboCount++;
            _lastTeaseTime = Time.time;
            if (_teaseComboCount >= TeaseClicksNeeded)
            {
                _manualMadUntilTime = Time.time + ManualMadDurationSeconds;
                _teaseComboCount = 0;
            }
        }

        private static bool IsEscapePressed()
        {
            return (GetAsyncKeyState(VirtualKeyEscape) & 0x8000) != 0;
        }

        private void ApplyVirusWallpaper()
        {
            if (_virusWallpaperActive)
            {
                return;
            }

            CaptureOriginalWallpaper();
            string path = EnsureVirusWallpaperExists();
            SetWallpaper(path);
            _virusWallpaperActive = true;
        }

        private void RestoreWallpaper()
        {
            if (!_virusWallpaperActive || !_originalWallpaperCaptured)
            {
                return;
            }

            SetWallpaper(_originalWallpaper);
            SetDesktopStyle(_originalWallpaperStyle, _originalTileWallpaper);
            _virusWallpaperActive = false;
        }

        private void CaptureOriginalWallpaper()
        {
            if (_originalWallpaperCaptured)
            {
                return;
            }

            using (RegistryKey desktopKey = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", false))
            {
                _originalWallpaper = (string)desktopKey?.GetValue("WallPaper", "") ?? "";
                _originalWallpaperStyle = (string)desktopKey?.GetValue("WallpaperStyle", "10") ?? "10";
                _originalTileWallpaper = (string)desktopKey?.GetValue("TileWallpaper", "0") ?? "0";
            }

            _originalWallpaperCaptured = true;
        }

        private string EnsureVirusWallpaperExists()
        {
            if (!string.IsNullOrEmpty(_virusWallpaperPath) && File.Exists(_virusWallpaperPath))
            {
                return _virusWallpaperPath;
            }

            string tempFile = Path.Combine(Path.GetTempPath(), "goose_fake_virus_wallpaper.bmp");
            using (Bitmap bmp = new Bitmap(1920, 1080))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.FromArgb(20, 0, 0));

                for (int i = 0; i < 600; i++)
                {
                    int x = _random.Next(0, bmp.Width);
                    int y = _random.Next(0, bmp.Height);
                    int w = _random.Next(10, 260);
                    int h = _random.Next(2, 16);
                    using (SolidBrush noiseBrush = new SolidBrush(Color.FromArgb(_random.Next(20, 100), 255, 0, 0)))
                    {
                        g.FillRectangle(noiseBrush, x, y, w, h);
                    }
                }

                using (Font bigFont = new Font("Consolas", 64, FontStyle.Bold))
                using (Font smallFont = new Font("Consolas", 30, FontStyle.Bold))
                using (SolidBrush redBrush = new SolidBrush(Color.FromArgb(235, 255, 35, 35)))
                {
                    g.DrawString("SYSTEM BREACH", bigFont, redBrush, 80, 120);
                    g.DrawString("HONK.EXE ACTIVE", smallFont, redBrush, 90, 240);
                    g.DrawString("Press ESC to restore desktop", smallFont, Brushes.White, 90, 300);
                }

                bmp.Save(tempFile, ImageFormat.Bmp);
            }

            _virusWallpaperPath = tempFile;
            return _virusWallpaperPath;
        }

        private static void SetWallpaper(string path)
        {
            SystemParametersInfo(20, 0, path, 0x01 | 0x02);
            SetDesktopStyle("2", "0");
        }

        private static void SetDesktopStyle(string wallpaperStyle, string tileWallpaper)
        {
            using (RegistryKey desktopKey = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", true))
            {
                desktopKey?.SetValue("WallpaperStyle", wallpaperStyle);
                desktopKey?.SetValue("TileWallpaper", tileWallpaper);
            }
        }

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int SystemParametersInfo(int action, int param, string vparam, int init);
    }
}
