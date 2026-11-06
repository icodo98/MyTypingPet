using System;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle(MyTypingPet.Config.AppName)]
[assembly: AssemblyProduct(MyTypingPet.Config.AppName)]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace MyTypingPet
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            bool isFirst;
            using (var mutex = new Mutex(true, Config.AppId + ".SingleInstance", out isFirst))
            {
                if (!isFirst)
                {
                    // 이미 켜져 있으면, 켜져 있는 펫에게 "화면으로 돌아와" 신호만 보내고 끝낸다.
                    try
                    {
                        using (EventWaitHandle ev = EventWaitHandle.OpenExisting(Config.AppId + ".BringBack"))
                            ev.Set();
                    }
                    catch { }
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                SpriteSet sprites;
                try
                {
                    sprites = SpriteSet.Load();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, Config.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                using (var bringBack = new EventWaitHandle(false, EventResetMode.AutoReset, Config.AppId + ".BringBack"))
                using (var form = new PetForm(sprites, bringBack))
                {
                    Application.Run(form);
                }
            }
        }
    }
}
