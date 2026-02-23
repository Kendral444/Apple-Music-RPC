
using System;
using System.Diagnostics;
using System.IO;

class Program {
    static void Main() {
        try {
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            string target = Path.Combine(appDir, "core-bin.exe");
            
            if (File.Exists(target)) {
                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.FileName = target;
                // Core is dumb now, it just runs. We don't need --silent as we control the window here.
                // But we pass it if we want to be consistent, though Core ignores it in V21.
                startInfo.UseShellExecute = false;
                startInfo.CreateNoWindow = true; 
                startInfo.WindowStyle = ProcessWindowStyle.Hidden;
                Process.Start(startInfo);
            }
        } catch {}
    }
}
