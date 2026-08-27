using Autodesk.Revit.UI;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

[assembly: AssemblyTitle("DGH Tools for Autodesk Revit 2023")]
[assembly: AssemblyDescription("DGH Tools add-in for Autodesk Revit 2023.")]
[assembly: AssemblyCompany("DGH Tools")]
[assembly: AssemblyProduct("DGH Tools for Autodesk Revit")]
[assembly: AssemblyCopyright("Copyright © DGH Tools 2026")]
[assembly: AssemblyVersion("0.8.1.0")]
[assembly: AssemblyFileVersion("0.8.1.0")]
[assembly: AssemblyInformationalVersion("0.8.1")]

namespace DghTools.Revit
{
    public class App : IExternalApplication
    {
        private const string TabName = "DGH Tools";
        private const string PanelName = "Grids";

        public Result OnStartup(UIControlledApplication application)
        {
            try { application.CreateRibbonTab(TabName); } catch { }

            RibbonPanel panel = application.GetRibbonPanels(TabName)
                .FirstOrDefault(p => p.Name == PanelName)
                ?? application.CreateRibbonPanel(TabName, PanelName);

            string assemblyPath = Assembly.GetExecutingAssembly().Location;
            string dir = Path.GetDirectoryName(assemblyPath);
            string assetsDir = Path.Combine(dir, "Assets");

            EnsureGridElbowIcon(Path.Combine(assetsDir, "AddGridElbows_16.png"), 16);
            EnsureGridElbowIcon(Path.Combine(assetsDir, "AddGridElbows_32.png"), 32);

            var alignData = new PushButtonData(
                "DghTools.AlignGridEnds",
                "Align Grid\nEnds",
                assemblyPath,
                typeof(AlignGridEndsCommand).FullName);

            var alignButton = panel.AddItem(alignData) as PushButton;
            if (alignButton != null)
            {
                alignButton.ToolTip = "Align selected grid ends in the active view.";
                alignButton.LongDescription = "Select two or more parallel straight grids, run the command, and pick the new extent position. Only the edited end becomes View Specific (2D) in the active view.";
                alignButton.Image = LoadImage(Path.Combine(assetsDir, "AlignGridEnds_16.png"));
                alignButton.LargeImage = LoadImage(Path.Combine(assetsDir, "AlignGridEnds_32.png"));
            }

            var elbowData = new PushButtonData(
                "DghTools.AddGridElbows",
                "Add Grid\nElbows",
                assemblyPath,
                typeof(AddGridElbowsCommand).FullName);

            var elbowButton = panel.AddItem(elbowData) as PushButton;
            if (elbowButton != null)
            {
                elbowButton.ToolTip = "Separate clashing grid bubbles with elbows.";
                elbowButton.LongDescription = "Adds or adjusts leaders only where selected grid bubbles are too close. Moved bubbles stay on the same common annotation line. Minimum spacing is scale-aware.";
                elbowButton.Image = LoadImage(Path.Combine(assetsDir, "AddGridElbows_16.png"));
                elbowButton.LargeImage = LoadImage(Path.Combine(assetsDir, "AddGridElbows_32.png"));
            }

            UpdateManager.BeginBackgroundCheck();
            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            UpdateManager.TryLaunchPendingUpdate(Assembly.GetExecutingAssembly().Location);
            return Result.Succeeded;
        }

        private static ImageSource LoadImage(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;

                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    BitmapDecoder decoder = BitmapDecoder.Create(
                        stream,
                        BitmapCreateOptions.PreservePixelFormat,
                        BitmapCacheOption.OnLoad);

                    BitmapFrame frame = decoder.Frames[0];
                    frame.Freeze();
                    return frame;
                }
            }
            catch { return null; }
        }

        private static void EnsureGridElbowIcon(string path, int size)
        {
            try
            {
                if (File.Exists(path)) return;

                string folder = Path.GetDirectoryName(path);
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

                DrawingVisual visual = new DrawingVisual();
                using (DrawingContext dc = visual.RenderOpen())
                {
                    double k = size / 32.0;
                    Pen gridPen = new Pen(new SolidColorBrush(Color.FromRgb(36, 82, 126)), 1.8 * k);
                    Pen accentPen = new Pen(new SolidColorBrush(Color.FromRgb(31, 144, 255)), 2.0 * k);
                    Brush accentBrush = new SolidColorBrush(Color.FromRgb(31, 144, 255));

                    gridPen.Freeze();
                    accentPen.Freeze();
                    accentBrush.Freeze();

                    double[] x = new double[] { 8 * k, 16 * k, 24 * k };
                    for (int i = 0; i < x.Length; i++)
                    {
                        dc.DrawLine(gridPen, new Point(x[i], 10 * k), new Point(x[i], 28 * k));
                    }

                    dc.DrawLine(accentPen, new Point(8 * k, 10 * k), new Point(8 * k, 7 * k));
                    dc.DrawLine(accentPen, new Point(8 * k, 7 * k), new Point(5 * k, 7 * k));
                    dc.DrawEllipse(null, accentPen, new Point(4 * k, 7 * k), 2.7 * k, 2.7 * k);

                    dc.DrawLine(accentPen, new Point(16 * k, 10 * k), new Point(16 * k, 7 * k));
                    dc.DrawEllipse(null, accentPen, new Point(16 * k, 7 * k), 2.7 * k, 2.7 * k);

                    dc.DrawLine(accentPen, new Point(24 * k, 10 * k), new Point(24 * k, 7 * k));
                    dc.DrawLine(accentPen, new Point(24 * k, 7 * k), new Point(27 * k, 7 * k));
                    dc.DrawEllipse(null, accentPen, new Point(28 * k, 7 * k), 2.7 * k, 2.7 * k);

                    dc.DrawEllipse(accentBrush, null, new Point(8 * k, 28 * k), 1.8 * k, 1.8 * k);
                    dc.DrawEllipse(accentBrush, null, new Point(16 * k, 28 * k), 1.8 * k, 1.8 * k);
                    dc.DrawEllipse(accentBrush, null, new Point(24 * k, 28 * k), 1.8 * k, 1.8 * k);
                }

                RenderTargetBitmap bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(visual);

                PngBitmapEncoder encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));

                using (FileStream stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                    encoder.Save(stream);
            }
            catch { }
        }
    }
}
