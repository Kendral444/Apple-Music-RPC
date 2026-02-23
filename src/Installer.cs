
using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using System.Reflection;
using System.Runtime.InteropServices;

class Installer {
    [STAThread]
    static void Main() {
        try {
            // 1. Setup Paths
            string appName = "AppleMusicRPC";
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string targetDir = Path.Combine(appData, appName);
            string currentDir = AppDomain.CurrentDomain.BaseDirectory;
            
            string coreName = "AppleMusicRPC-Core.exe";
            string launcherName = "AppleMusicRPC.exe"; 
            string iconName = "meow.ico";

            // 2. Kill Existing
            KillProcess("AppleMusicRPC");
            KillProcess("AppleMusicRPC-Core");

            // 3. Create Directory
            if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);

            // 4. Copy Files
            string[] files = new string[] { coreName, launcherName, iconName };
            foreach (string file in files) {
                string src = Path.Combine(currentDir, file);
                string dst = Path.Combine(targetDir, file);
                if (File.Exists(src)) File.Copy(src, dst, true);
            }

            // 5. Create Shortcuts (Startup & Desktop)
            string iconPath = Path.Combine(targetDir, iconName);
            string exePath = Path.Combine(targetDir, launcherName);
            
            CreateShortcut("Startup", exePath, iconPath);
            CreateShortcut("Desktop", exePath, iconPath);

            // 6. Completion Dialog
            DialogResult result = MessageBox.Show(
                "L'installation est terminee avec succes !\n\nVoulez-vous lancer Apple Music RPC maintenant ?", 
                "Apple Music RPC Installer", 
                MessageBoxButtons.YesNo, 
                MessageBoxIcon.Information
            );

            if (result == DialogResult.Yes) {
                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.FileName = exePath;
                startInfo.UseShellExecute = false;
                startInfo.WindowStyle = ProcessWindowStyle.Hidden;
                Process.Start(startInfo);
            }

        } catch (Exception e) {
            MessageBox.Show("Erreur d'installation: " + e.Message, "Erreur", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    static void KillProcess(string name) {
        try {
            foreach (Process proc in Process.GetProcessesByName(name)) {
                try { proc.Kill(); proc.WaitForExit(1000); } catch {}
            }
        } catch {}
    }

    static void CreateShortcut(string folderType, string targetPath, string iconPath) {
        try {
            string linkPath = "";
            if (folderType == "Startup") {
                linkPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Apple Music RPC.lnk");
            } else if (folderType == "Desktop") {
                linkPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "Apple Music RPC.lnk");
            }

            // Using Concatenation instead of Interpolation for C# 4.0 compat
            string targetDir = Path.GetDirectoryName(targetPath);
            string psScript = "$WshShell = New-Object -comObject WScript.Shell; " +
                              "$Shortcut = $WshShell.CreateShortcut('" + linkPath + "'); " +
                              "$Shortcut.TargetPath = '" + targetPath + "'; " +
                              "$Shortcut.IconLocation = '" + iconPath + "'; " +
                              "$Shortcut.Description = 'Apple Music RPC'; " +
                              "$Shortcut.WorkingDirectory = '" + targetDir + "'; " +
                              "$Shortcut.Save()";

            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = "powershell";
            psi.Arguments = "-NoProfile -ExecutionPolicy Bypass -Command \"" + psScript + "\"";
            psi.WindowStyle = ProcessWindowStyle.Hidden;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            Process.Start(psi).WaitForExit();
        } catch {}
    }
}
