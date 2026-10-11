// 界面语言（中文 / 日本語 / English）。选择保存在 %APPDATA%\PmmCamKit\lang.txt。
using System;
using System.Globalization;
using System.IO;

namespace PmmCamKit
{
    static class L
    {
        public static readonly string[] Names = { "中文", "日本語", "English" };
        public static int Cur;   // 0 = 中文，1 = 日本語，2 = English

        public static string T(string zh, string ja, string en) { return Cur == 1 ? ja : Cur == 2 ? en : zh; }

        static string SettingsFile
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PmmCamKit", "lang.txt"); }
        }

        public static void Load()
        {
            try
            {
                int v;
                if (File.Exists(SettingsFile) && int.TryParse(File.ReadAllText(SettingsFile).Trim(), out v) && v >= 0 && v < Names.Length)
                {
                    Cur = v; return;
                }
            }
            catch (Exception) { }
            string iso = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            Cur = iso == "zh" ? 0 : iso == "ja" ? 1 : 2;
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile));
                File.WriteAllText(SettingsFile, Cur.ToString());
            }
            catch (Exception) { }
        }
    }
}
