using System;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MyTypingPet
{
    // 크기·위치 같은 설정을 HKCU\Software\<AppId> 에 저장한다.
    class Settings
    {
        const string KeyPath = @"Software\" + Config.AppId;

        public int SizePercent = Config.DefaultSizePercent;
        public int RestMs = Config.DefaultRestMs;
        public bool OnTop = true;
        public bool Fixed = false;
        public int? X, Y;

        public static Settings Load()
        {
            var s = new Settings();
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(KeyPath))
                {
                    if (k == null) return s;
                    s.SizePercent = Clamp((int)k.GetValue("size", s.SizePercent), 10, 1000);
                    s.RestMs = Clamp((int)k.GetValue("restMs", s.RestMs), 100, 600000);
                    s.OnTop = (int)k.GetValue("onTop", 1) != 0;
                    s.Fixed = (int)k.GetValue("fixed", 0) != 0;
                    object x = k.GetValue("x"), y = k.GetValue("y");
                    if (x is int && y is int) { s.X = (int)x; s.Y = (int)y; }
                }
            }
            catch
            {
                return new Settings();   // 값이 깨져 있으면 기본값으로
            }
            return s;
        }

        public void Save()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(KeyPath))
                {
                    k.SetValue("size", SizePercent, RegistryValueKind.DWord);
                    k.SetValue("restMs", RestMs, RegistryValueKind.DWord);
                    k.SetValue("onTop", OnTop ? 1 : 0, RegistryValueKind.DWord);
                    k.SetValue("fixed", Fixed ? 1 : 0, RegistryValueKind.DWord);
                    if (X.HasValue && Y.HasValue)
                    {
                        k.SetValue("x", X.Value, RegistryValueKind.DWord);
                        k.SetValue("y", Y.Value, RegistryValueKind.DWord);
                    }
                }
            }
            catch { /* 저장 실패는 조용히 무시 */ }
        }

        static int Clamp(int v, int min, int max) { return Math.Max(min, Math.Min(max, v)); }
    }

    // "Windows 시작 시 자동 실행" = HKCU\...\Run 에 exe 경로를 등록
    static class AutoStart
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        static string Command { get { return "\"" + Application.ExecutablePath + "\""; } }

        public static bool Enabled
        {
            get
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey))
                {
                    string v = k == null ? null : k.GetValue(Config.AppId) as string;
                    return v != null && string.Equals(v, Command, StringComparison.OrdinalIgnoreCase);
                }
            }
            set
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (value) k.SetValue(Config.AppId, Command);
                    else k.DeleteValue(Config.AppId, false);
                }
            }
        }
    }
}
