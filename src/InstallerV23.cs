
using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using System.Reflection;

class Installer {
    [STAThread]
    static void Main() {
        string currentStep = "Start";
        try {
            // 1. Setup Paths
            string appName = "AppleMusicRPC";
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string targetDir = Path.Combine(appData, appName);
            string currentDir = AppDomain.CurrentDomain.BaseDirectory;
            
            string coreName = "AppleMusicRPC-Core.exe";
            string launcherName = "AppleMusicRPC.exe"; 
            string iconName = "meow.ico";

            currentStep = "Directory Creation";
            if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);

            // 2. Kill Existing
            currentStep = "Killing Processes";
            KillProcess("AppleMusicRPC");
            KillProcess("AppleMusicRPC-Core");

            // 3. Copy Files
            string[] files = new string[] { coreName, launcherName, iconName };
            foreach (string file in files) {
                currentStep = "Copying " + file;
                string src = Path.Combine(currentDir, file);
                string dst = Path.Combine(targetDir, file);
                
                if (File.Exists(src)) {
                    // Try to delete if exists (to avoid overwrite locks if possible)
                    if (File.Exists(dst)) {
                        try { File.Delete(dst); } catch {}
                    }
                    File.Copy(src, dst, true);
                    
                    if (!File.Exists(dst)) {
                        throw new FileNotFoundException("Validation failed: File not found after copy: " + dst);
                    }
                } else {
                    // If source is missing, warn but continue (might be running from zip?)
                    // MessageBox.Show("Source file missing: " + src);
                }
            }

            // 4. Create Shortcuts (Startup & Desktop)
            string iconPath = Path.Combine(targetDir, iconName);
            string exePath = Path.Combine(targetDir, launcherName);
            
            currentStep = "Creating Shortcuts";
            CreateShortcut("Startup", exePath, iconPath);
            CreateShortcut("Desktop", exePath, iconPath);

            // 5. Completion Dialog
            currentStep = "Launching";
            DialogResult result = MessageBox.Show(
                "L'installation est terminee avec succes !\n\nVoulez-vous lancer Apple Music RPC maintenant ?", 
                "Apple Music RPC Installer", 
                MessageBoxButtons.YesNo, 
                MessageBoxIcon.Information
            );

            if (result == DialogResult.Yes) {
                if (File.Exists(exePath)) {
                    ProcessStartInfo startInfo = new ProcessStartInfo();
                    startInfo.FileName = exePath;
                    startInfo.UseShellExecute = false;
                    startInfo.WindowStyle = ProcessWindowStyle.Hidden;
                    Process.Start(startInfo);
                } else {
                    MessageBox.Show("Erreur: Le lanceur n'est pas installe correctment.\n" + exePath);
                }
            }

        } catch (Exception e) {
            MessageBox.Show("Erreur a l'etape '" + currentStep + "':\n" + e.Message + "\n\nStack:\n" + e.StackTrace, "Erreur Critique", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
            
            string targetDir = Path.GetDirectoryName(targetPath);
            
            // Simplified PowerShell logic
            string psScript = "$WshShell = New-Object -comObject WScript.Shell; " +
                              "$Shortcut = $WshShell.CreateShortcut('" + linkPath + "'); " +
                              "$Shortcut.TargetPath = '" + targetPath + "'; " +
                              "$Shortcut.IconLocation = '" + iconPath + "'; " +
                              "$Shortcut.WorkingDirectory = '" + targetDir + "'; " +
                              "$Shortcut.Save()";

            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = "powershell";
            psi.Arguments = "-NoProfile -ExecutionPolicy Bypass -Command \"" + psScript + "\"";
            psi.WindowStyle = ProcessWindowStyle.Hidden;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            Process.Start(psi).WaitForExit();
        } catch (Exception e) {
             // MessageBox.Show("Shortcut Error: " + e.Message);
        }
    }
}
