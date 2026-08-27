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
[assembly: AssemblyVersion("0.8.0.0")]
[assembly: AssemblyFileVersion("0.8.0.0")]
[assembly: AssemblyInformationalVersion("0.8.0")]

namespace DghTools.Revit
{
    public class App : IExternalApplication
    {
        private const string TabName = "DGH Tools";
        private const string PanelName = "Grids";

        private const string GridElbow16 = "iVBORw0KGgoAAAANSUhEUgAAABAAAAAQCAYAAAAf8/9hAAAB50lEQVR4nJWTPWvTURTGf8+9+Seh1aiVqlNCoSCIixiyiPRT6AewurgUN9Eh+W8Kheg3qKPNUsRBcKmj74q2glRBKaW0hNhWS9Im9zgEmzQvg2c853cO93nuOaI3zDS1iP/9Fb1do0Ws0Md0cb1Z9YFFcwO6D7hOg5mQ7Ox9O1o/wg0CJ1o7PFq9o5V/tW5usmiZ/dNc00GyhLJpjinDM5+iEBpgYpsGUz9u8ZEiooRRQrnjZIh47kfJO4CpEp5YgRTTLk3BahTqvzgl449EmSJimbbmWMEc112SfLPKpV59Y7YHu+ssr9/VpjWpGjhiBc5hHbmMWQNL7vDZAbyAACYfMYeopSf4livbayU5ryQT2Qc2DcBiW673zOHZao7zpWNi0RyxQm7WLjDCPZpcpsWuIk6GJvVEnTPfb2uLefNcVSs7axfdCAuHv63L7cmHltmL8GrwFJGPxPjKjLYxEyCkkCvbk8MeSEbRHGZuZUbbP2+qhtgnBO9HuzyANkdI9y9JrIAUmDePmXBEJAbsUqyAd25ApR1XKhWQzH96/D7xYc75d5X2CwSU2guYWKq8SQwbsLGxJAC/9qpqUVJEqU5xuSIAt/pyc+iAA7GpTAInoP+mLJWJhkroUC3DBh8k1hp0af8XfwGupMZH9e+iHgAAAABJRU5ErkJggg==";
        private const string GridElbow32 = "iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAAC10lEQVR4nO2XzWtUZxTGf+d958uo8QNRKDW3SkCooovSLNyk6N+QfuwK0koLxbqti8ldSUGYUUSsi2KXzfwDhS6aWYhgsZoWQxFtqUgI2iHGj3RGc9/HxcwkcT6sSROoMM/yPj8Ozz3ve87lGq2SbHgc//gmdnWKhNhCG7MWXIO2tkd5ubXkFgHJMNOer7WxuoFPCGxJHvHd3a/sVtNbC84W4FFsIMcm6+cHn2Uo1EDGQ2oM/3WcCfIYo2hVudiCAxgexRNbIMsRl2NIMwxVH7DdxBMzCuQxJrHh8S5c4AmOU2Ba4AByfOpfwgG0nttWPYW5aSanT9h9zVMROGILvI1auUzg9+kTdl8JFYmtAAtcbEHCQpXkZZwDKEMAmU9zEWMmt4vbUUE/W4Z9lmHXQFFHABivH1eTq/Xxa1TUJbeefcBJgHfewJffI4kK+tj18aE5fDXDRHRGE27DIsfepR2ILZDH/vzCbuoxh3FM4NmrKhUCO4Gzu9exsRzbPCVckxP0uXUcDLN8dOdLK5GXuzpFgpkEn/k0B5J/qODYYR6bn+WDJsf7lnSYmsWxGTyt/oFz2hIVdCkqqjZ4Wv0LzJg8QFTUj1FBNbPGeEnWHLO3ivopKujp4AW9ufsbbeo2hi/eATM1Crlbx+zhnc9tBuMZIXi/fskduIEahbIQ/MC32ty6YAQOBbdtM5U/jtoskmNMvpVrXx6xBcwCY/JIhiNNqsOOiS3g8HhHLqG9nY4UKcffNdKNzqpT2ztUrmukVAIz+d++v5a6ftH5X0r1DhiMTJYM4FW8zOVSwEy070QAUt0C3Lt3o15o6kpF6YyRzv5nb1kBmlK2P1Xvk1bNW6quR7BYKRHqUmSl3rICrLF6AXoBegF6AV6HABJSl5+JlXrLCmBZl866pDbX4YO6Uu8VApTLowmAmTufPJs/tD/77lyjsFbq/dur/k+VzztGxvyqej0t0XNN+NZH6YEFMwAAAABJRU5ErkJggg==";

        public Result OnStartup(UIControlledApplication application)
        {
            try { application.CreateRibbonTab(TabName); } catch { }

            RibbonPanel panel = application.GetRibbonPanels(TabName)
                .FirstOrDefault(p => p.Name == PanelName)
                ?? application.CreateRibbonPanel(TabName, PanelName);

            string assemblyPath = Assembly.GetExecutingAssembly().Location;
            string dir = Path.GetDirectoryName(assemblyPath);
            string assetsDir = Path.Combine(dir, "Assets");

            EnsureEmbeddedAsset(Path.Combine(assetsDir, "AddGridElbows_16.png"), GridElbow16);
            EnsureEmbeddedAsset(Path.Combine(assetsDir, "AddGridElbows_32.png"), GridElbow32);

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

        private static void EnsureEmbeddedAsset(string path, string base64)
        {
            try
            {
                string folder = Path.GetDirectoryName(path);
                if (!Directory.Exists(folder))
                    Directory.CreateDirectory(folder);

                File.WriteAllBytes(path, Convert.FromBase64String(base64));
            }
            catch { }
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
