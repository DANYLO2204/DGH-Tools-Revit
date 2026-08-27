using Autodesk.Revit.UI;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Media.Imaging;

[assembly: AssemblyTitle("DGH Tools for Autodesk Revit 2023")]
[assembly: AssemblyDescription("DGH Tools add-in for Autodesk Revit 2023.")]
[assembly: AssemblyCompany("DGH Tools")]
[assembly: AssemblyProduct("DGH Tools for Autodesk Revit")]
[assembly: AssemblyCopyright("Copyright © DGH Tools 2026")]
[assembly: AssemblyVersion("0.7.0.0")]
[assembly: AssemblyFileVersion("0.7.0.0")]
[assembly: AssemblyInformationalVersion("0.7.0")]

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
            var data = new PushButtonData(
                "DghTools.AlignGridEnds",
                "Align Grid\nEnds",
                assemblyPath,
                typeof(AlignGridEndsCommand).FullName);

            var button = panel.AddItem(data) as PushButton;
            if (button != null)
            {
                button.ToolTip = "Align selected grid ends in the active view.";
                button.LongDescription = "Select two or more parallel straight grids, run the command, and pick the new extent position. Only the edited end becomes View Specific (2D) in the active view.";

                string dir = Path.GetDirectoryName(assemblyPath);
                button.Image = LoadImage(Path.Combine(dir, "Assets", "AlignGridEnds_16.png"));
                button.LargeImage = LoadImage(Path.Combine(dir, "Assets", "AlignGridEnds_32.png"));
            }

            UpdateManager.BeginBackgroundCheck();
            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            UpdateManager.TryLaunchPendingUpdate(Assembly.GetExecutingAssembly().Location);
            return Result.Succeeded;
        }

        private static BitmapImage LoadImage(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.UriSource = new Uri(path, UriKind.Absolute);
                image.EndInit();
                image.Freeze();
                return image;
            }
            catch { return null; }
        }
    }
}
