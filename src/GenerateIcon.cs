using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;

class IconGenerator {
    static void Main() {
        int size = 256;
        using (Bitmap bmp = new Bitmap(size, size)) {
            using (Graphics g = Graphics.FromImage(bmp)) {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                
                // Fond sombre arrondi
                GraphicsPath path = new GraphicsPath();
                int radius = 40;
                path.AddArc(0, 0, radius, radius, 180, 90);
                path.AddArc(size - radius, 0, radius, radius, 270, 90);
                path.AddArc(size - radius, size - radius, radius, radius, 0, 90);
                path.AddArc(0, size - radius, radius, radius, 90, 90);
                path.CloseAllFigures();
                
                g.FillPath(new SolidBrush(Color.FromArgb(25, 25, 25)), path);
                
                // Cercle Apple Music Pink Red abstract
                using (LinearGradientBrush brush = new LinearGradientBrush(new Rectangle(20, 20, size - 40, size - 40), Color.FromArgb(250, 35, 59), Color.FromArgb(180, 20, 40), 45f)) {
                    g.FillEllipse(brush, new Rectangle(50, 50, size - 100, size - 100));
                }
                
                // Note de musique simple
                using (Pen pen = new Pen(Color.White, 15)) {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    pen.LineJoin = LineJoin.Round;
                    
                    g.DrawLine(pen, 110, 80, 110, 170); // Tige gauche
                    g.DrawLine(pen, 160, 70, 160, 150); // Tige droite
                    g.DrawLine(pen, 110, 80, 160, 70);  // Barre
                    g.FillEllipse(Brushes.White, 85, 150, 35, 30); // Base gauche
                    g.FillEllipse(Brushes.White, 135, 130, 35, 30); // Base droite
                }
            }
            
            // Sauvegarder en ICO simpliste via stream bitmap
            using (FileStream fs = new FileStream("app_icon.ico", FileMode.Create)) {
                Icon.FromHandle(bmp.GetHicon()).Save(fs);
            }
        }
    }
}
