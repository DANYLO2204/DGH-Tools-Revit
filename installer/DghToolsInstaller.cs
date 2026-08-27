using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace DghTools.Installer
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            Application.Run(new InstallerForm());
        }
    }

    internal sealed class InstallerForm : Form
    {
        private const string ManifestUrl = "https://raw.githubusercontent.com/DANYLO2204/DGH-Tools-Revit/main/update/update.json";
        private const string RepositoryUrl = "https://github.com/DANYLO2204/DGH-Tools-Revit";

        private readonly Label _revitValue;
        private readonly Label _installedValue;
        private readonly Label _latestValue;
        private readonly Label _status;
        private readonly ProgressBar _progress;
        private readonly Button _installButton;
        private readonly Button _repoButton;
        private readonly Panel _header;
        private UpdateManifest _manifest;
        private string _revitDir;

        public InstallerForm()
        {
            Text = "DGH Tools — Revit 2023 Installer";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = true;
            ClientSize = new Size(620, 455);
            BackColor = Color.FromArgb(244, 247, 251);
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

            _header = new Panel();
            _header.Dock = DockStyle.Top;
            _header.Height = 112;
            _header.BackColor = Color.FromArgb(24, 55, 91);
            _header.Paint += HeaderPaint;
            Controls.Add(_header);

            var title = new Label();
            title.AutoSize = true;
            title.Text = "DGH Tools";
            title.ForeColor = Color.White;
            title.Font = new Font("Segoe UI Semibold", 22F, FontStyle.Bold);
            title.Location = new Point(94, 22);
            _header.Controls.Add(title);

            var subtitle = new Label();
            subtitle.AutoSize = true;
            subtitle.Text = "Installer for Autodesk Revit 2023";
            subtitle.ForeColor = Color.FromArgb(205, 221, 240);
            subtitle.Font = new Font("Segoe UI", 10F);
            subtitle.Location = new Point(97, 65);
            _header.Controls.Add(subtitle);

            var info = new Label();
            info.AutoSize = false;
            info.Text = "The installer always downloads the newest published DGH Tools version from GitHub.";
            info.ForeColor = Color.FromArgb(69, 82, 98);
            info.Font = new Font("Segoe UI", 9.5F);
            info.Location = new Point(28, 132);
            info.Size = new Size(565, 38);
            Controls.Add(info);

            var cards = new Panel();
            cards.Location = new Point(28, 180);
            cards.Size = new Size(565, 112);
            cards.BackColor = Color.White;
            cards.BorderStyle = BorderStyle.FixedSingle;
            Controls.Add(MakeCaption("Revit 2023", 18, 14));
            Controls.Add(MakeCaption("Installed version", 200, 14));
            Controls.Add(MakeCaption("Latest version", 385, 14));

            _revitValue = MakeValue("Checking…", 18, 50);
            _installedValue = MakeValue("Checking…", 200, 50);
            _latestValue = MakeValue("Checking…", 385, 50);
            cards.Controls.Add(_revitValue);
            cards.Controls.Add(_installedValue);
            cards.Controls.Add(_latestValue);
            Controls.Add(cards);

            _status = new Label();
            _status.AutoSize = false;
            _status.Text = "Checking system and update channel…";
            _status.ForeColor = Color.FromArgb(63, 76, 92);
            _status.Location = new Point(28, 312);
            _status.Size = new Size(565, 24);
            Controls.Add(_status);

            _progress = new ProgressBar();
            _progress.Location = new Point(28, 342);
            _progress.Size = new Size(565, 10);
            _progress.Style = ProgressBarStyle.Continuous;
            _progress.Minimum = 0;
            _progress.Maximum = 100;
            Controls.Add(_progress);

            _installButton = new Button();
            _installButton.Text = "Install / Update";
            _installButton.Enabled = false;
            _installButton.FlatStyle = FlatStyle.Flat;
            _installButton.FlatAppearance.BorderSize = 0;
            _installButton.BackColor = Color.FromArgb(31, 119, 208);
            _installButton.ForeColor = Color.White;
            _installButton.Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold);
            _installButton.Location = new Point(28, 376);
            _installButton.Size = new Size(380, 46);
            _installButton.Cursor = Cursors.Hand;
            _installButton.Click += InstallClicked;
            Controls.Add(_installButton);

            _repoButton = new Button();
            _repoButton.Text = "GitHub";
            _repoButton.FlatStyle = FlatStyle.Flat;
            _repoButton.FlatAppearance.BorderColor = Color.FromArgb(187, 198, 212);
            _repoButton.BackColor = Color.White;
            _repoButton.ForeColor = Color.FromArgb(54, 68, 84);
            _repoButton.Location = new Point(422, 376);
            _repoButton.Size = new Size(171, 46);
            _repoButton.Cursor = Cursors.Hand;
            _repoButton.Click += delegate { Process.Start(new ProcessStartInfo(RepositoryUrl) { UseShellExecute = true }); };
            Controls.Add(_repoButton);

            Shown += delegate { BeginStatusCheck(); };
        }

        private Label MakeCaption(string text, int x, int y)
        {
            var label = new Label();
            label.AutoSize = true;
            label.Text = text;
            label.ForeColor = Color.FromArgb(111, 125, 142);
            label.Font = new Font("Segoe UI", 8.5F);
            label.Location = new Point(x, y);
            return label;
        }

        private Label MakeValue(string text, int x, int y)
        {
            var label = new Label();
            label.AutoSize = true;
            label.Text = text;
            label.ForeColor = Color.FromArgb(32, 45, 60);
            label.Font = new Font("Segoe UI Semibold", 12.5F, FontStyle.Bold);
            label.Location = new Point(x, y);
            return label;
        }

        private void HeaderPaint(object sender, PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(Color.FromArgb(31, 119, 208)))
                e.Graphics.FillEllipse(b, 28, 23, 50, 50);
            using (var p = new Pen(Color.White, 2.4F))
            {
                e.Graphics.DrawLine(p, 43, 36, 43, 60);
                e.Graphics.DrawLine(p, 52, 32, 52, 60);
                e.Graphics.DrawLine(p, 61, 39, 61, 60);
                e.Graphics.DrawLine(p, 37, 60, 67, 60);
                e.Graphics.DrawEllipse(p, 39, 30, 8, 8);
                e.Graphics.DrawEllipse(p, 48, 26, 8, 8);
                e.Graphics.DrawEllipse(p, 57, 33, 8, 8);
            }
        }

        private void BeginStatusCheck()
        {
            Task.Run(delegate
            {
                try
                {
                    _revitDir = DetectRevit2023();
                    string installed = ReadInstalledVersion();
                    _manifest = DownloadManifest();

                    BeginInvoke((Action)delegate
                    {
                        _revitValue.Text = _revitDir == null ? "Not found" : "Detected";
                        _revitValue.ForeColor = _revitDir == null ? Color.FromArgb(180, 65, 65) : Color.FromArgb(35, 133, 86);
                        _installedValue.Text = String.IsNullOrWhiteSpace(installed) ? "Not installed" : installed;
                        _latestValue.Text = _manifest == null ? "Unavailable" : _manifest.version;

                        if (_revitDir == null)
                        {
                            _status.Text = "Autodesk Revit 2023 was not found in the standard installation folder.";
                            _installButton.Enabled = false;
                        }
                        else if (_manifest == null || !_manifest.enabled)
                        {
                            _status.Text = "Could not read the current DGH Tools version from GitHub.";
                            _installButton.Enabled = false;
                        }
                        else
                        {
                            _status.Text = "Ready. Click Install / Update to install the latest published version.";
                            _installButton.Enabled = true;
                        }
                    });
                }
                catch (Exception ex)
                {
                    BeginInvoke((Action)delegate
                    {
                        _status.Text = "Check failed: " + ex.Message;
                        _installButton.Enabled = false;
                    });
                }
            });
        }

        private void InstallClicked(object sender, EventArgs e)
        {
            if (Process.GetProcessesByName("Revit").Length > 0)
            {
                MessageBox.Show(this, "Close Revit before installing or updating DGH Tools.", "DGH Tools", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _installButton.Enabled = false;
            _repoButton.Enabled = false;
            _progress.Value = 0;
            _status.Text = "Starting installation…";

            Task.Run(delegate
            {
                try
                {
                    InstallLatest();
                    BeginInvoke((Action)delegate
                    {
                        _progress.Value = 100;
                        _status.Text = "DGH Tools " + _manifest.version + " installed successfully. Start Revit 2023.";
                        _installedValue.Text = _manifest.version;
                        _installButton.Text = "Installed";
                        _repoButton.Enabled = true;
                        MessageBox.Show(this,
                            "DGH Tools " + _manifest.version + " has been installed successfully.\n\nOpen Revit 2023 and use: DGH Tools → Grids → Align Grid Ends.",
                            "Installation complete",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                    });
                }
                catch (Exception ex)
                {
                    BeginInvoke((Action)delegate
                    {
                        _status.Text = "Installation failed.";
                        _installButton.Enabled = true;
                        _repoButton.Enabled = true;
                        MessageBox.Show(this, ex.Message, "Installation failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    });
                }
            });
        }

        private void InstallLatest()
        {
            SetProgress(5, "Refreshing latest version from GitHub…");
            _manifest = DownloadManifest();
            if (_manifest == null || !_manifest.enabled)
                throw new InvalidOperationException("The GitHub update channel is unavailable or disabled.");
            if (!String.Equals(_manifest.revit, "2023", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The current DGH Tools package is not intended for Revit 2023.");
            if (_manifest.files == null || _manifest.files.Count == 0)
                throw new InvalidOperationException("The update manifest does not contain source files.");

            _revitDir = DetectRevit2023();
            if (_revitDir == null)
                throw new InvalidOperationException("Autodesk Revit 2023 could not be detected.");

            string revitApi = Path.Combine(_revitDir, "RevitAPI.dll");
            string revitApiUi = Path.Combine(_revitDir, "RevitAPIUI.dll");
            if (!File.Exists(revitApi) || !File.Exists(revitApiUi))
                throw new InvalidOperationException("Revit 2023 API files were not found.");

            string temp = Path.Combine(Path.GetTempPath(), "DGH_Tools_Installer_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            try
            {
                SetProgress(15, "Downloading current plugin source…");
                var sourceFiles = new List<string>();
                int index = 0;
                foreach (UpdateFile file in _manifest.files)
                {
                    if (file == null || String.IsNullOrWhiteSpace(file.name) || String.IsNullOrWhiteSpace(file.url))
                        throw new InvalidOperationException("The update manifest contains an invalid source entry.");
                    string name = Path.GetFileName(file.name);
                    if (!name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Unexpected update source file: " + name);
                    string path = Path.Combine(temp, name);
                    DownloadFile(file.url, path);
                    if (!File.Exists(path) || new FileInfo(path).Length < 100)
                        throw new InvalidOperationException("Downloaded source file is invalid: " + name);
                    sourceFiles.Add(path);
                    index++;
                    SetProgress(15 + Math.Min(20, index * 6), "Downloaded " + name);
                }

                string updater = Path.Combine(temp, "ApplyUpdate.ps1");
                if (String.IsNullOrWhiteSpace(_manifest.updater_url))
                    throw new InvalidOperationException("The update manifest does not specify the updater script.");
                DownloadFile(_manifest.updater_url, updater);
                if (!File.Exists(updater) || new FileInfo(updater).Length < 500)
                    throw new InvalidOperationException("Downloaded updater script is invalid.");

                SetProgress(40, "Preparing local Revit 2023 build…");
                string csc = FindCompiler();
                string presentationCore = FindFrameworkDll("PresentationCore.dll");
                string windowsBase = FindFrameworkDll("WindowsBase.dll");
                string webExtensions = FindFrameworkDll("System.Web.Extensions.dll");
                if (csc == null || presentationCore == null || windowsBase == null || webExtensions == null)
                    throw new InvalidOperationException("Required .NET Framework 4.x compiler/reference files are missing.");

                string newDll = Path.Combine(temp, "GridEndAligner.dll");
                string arguments = BuildCompilerArguments(newDll, revitApi, revitApiUi, presentationCore, windowsBase, webExtensions, sourceFiles);

                SetProgress(50, "Compiling DGH Tools " + _manifest.version + "…");
                var psi = new ProcessStartInfo();
                psi.FileName = csc;
                psi.Arguments = arguments;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                var process = Process.Start(psi);
                string stdout = process.StandardOutput.ReadToEnd();
                string stderr = process.StandardError.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode != 0 || !File.Exists(newDll))
                    throw new InvalidOperationException("Plugin compilation failed.\n\n" + stdout + "\n" + stderr);

                SetProgress(70, "Installing plugin files…");
                string addinRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Autodesk", "Revit", "Addins", "2023");
                string pluginDir = Path.Combine(addinRoot, "GridEndAligner");
                string assetsDir = Path.Combine(pluginDir, "Assets");
                Directory.CreateDirectory(pluginDir);
                Directory.CreateDirectory(assetsDir);

                string installedDll = Path.Combine(pluginDir, "GridEndAligner.dll");
                if (File.Exists(installedDll))
                {
                    try { File.Copy(installedDll, Path.Combine(pluginDir, "GridEndAligner.dll.bak"), true); }
                    catch { }
                }

                File.Copy(newDll, installedDll, true);
                File.Copy(updater, Path.Combine(pluginDir, "ApplyUpdate.ps1"), true);
                File.WriteAllText(Path.Combine(pluginDir, "version.txt"), _manifest.version, Encoding.ASCII);
                CreateRibbonIcons(assetsDir);

                SetProgress(82, "Registering add-in in Revit 2023…");
                string addinPath = Path.Combine(addinRoot, "GridEndAligner.addin");
                string xml = "<?xml version=\"1.0\" encoding=\"utf-8\" standalone=\"no\"?>\r\n" +
                             "<RevitAddIns>\r\n" +
                             "  <AddIn Type=\"Application\">\r\n" +
                             "    <Name>DGH Tools</Name>\r\n" +
                             "    <Assembly>" + EscapeXml(installedDll) + "</Assembly>\r\n" +
                             "    <AddInId>7D3D0B3F-66E8-45D7-8D8D-2A1EFD1D2C73</AddInId>\r\n" +
                             "    <FullClassName>DghTools.Revit.App</FullClassName>\r\n" +
                             "    <VendorId>DGH</VendorId>\r\n" +
                             "    <VendorDescription>DGH Tools for Autodesk Revit</VendorDescription>\r\n" +
                             "  </AddIn>\r\n" +
                             "</RevitAddIns>\r\n";
                File.WriteAllText(addinPath, xml, new UTF8Encoding(false));

                SetProgress(90, "Cleaning previous update state…");
                string stateDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DGH Tools", "GridEndAligner", "Updates");
                if (Directory.Exists(stateDir))
                {
                    foreach (string path in Directory.GetFiles(stateDir, "pending-*"))
                    {
                        try { File.Delete(path); } catch { }
                    }
                    try { File.Delete(Path.Combine(stateDir, "last-check.txt")); } catch { }
                }

                if (!File.Exists(installedDll) || !File.Exists(addinPath))
                    throw new InvalidOperationException("Installation verification failed.");

                SetProgress(98, "Installation verified.");
            }
            finally
            {
                try { Directory.Delete(temp, true); } catch { }
            }
        }

        private UpdateManifest DownloadManifest()
        {
            string url = ManifestUrl + "?t=" + DateTime.UtcNow.Ticks;
            using (var client = NewWebClient())
            {
                string json = client.DownloadString(url);
                var serializer = new JavaScriptSerializer();
                return serializer.Deserialize<UpdateManifest>(json);
            }
        }

        private void DownloadFile(string url, string path)
        {
            using (var client = NewWebClient())
                client.DownloadFile(url + (url.Contains("?") ? "&" : "?") + "t=" + DateTime.UtcNow.Ticks, path);
        }

        private WebClient NewWebClient()
        {
            var client = new WebClient();
            client.Headers[HttpRequestHeader.UserAgent] = "DGH-Tools-Revit-Installer/1.0";
            client.Headers[HttpRequestHeader.CacheControl] = "no-cache";
            return client;
        }

        private string DetectRevit2023()
        {
            string standard = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Autodesk", "Revit 2023");
            if (File.Exists(Path.Combine(standard, "RevitAPI.dll")) && File.Exists(Path.Combine(standard, "RevitAPIUI.dll")))
                return standard;
            return null;
        }

        private string ReadInstalledVersion()
        {
            try
            {
                string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Autodesk", "Revit", "Addins", "2023", "GridEndAligner", "version.txt");
                return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
            }
            catch { return null; }
        }

        private string FindCompiler()
        {
            string[] candidates = new string[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Microsoft.NET", "Framework64", "v4.0.30319", "csc.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Microsoft.NET", "Framework", "v4.0.30319", "csc.exe")
            };
            return candidates.FirstOrDefault(File.Exists);
        }

        private string FindFrameworkDll(string name)
        {
            string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string[] roots = new string[]
            {
                Path.Combine(programFilesX86, "Reference Assemblies", "Microsoft", "Framework", ".NETFramework", "v4.8"),
                Path.Combine(programFilesX86, "Reference Assemblies", "Microsoft", "Framework", ".NETFramework", "v4.7.2"),
                Path.Combine(windows, "Microsoft.NET", "Framework64", "v4.0.30319"),
                Path.Combine(windows, "Microsoft.NET", "Framework", "v4.0.30319")
            };
            foreach (string root in roots)
            {
                string direct = Path.Combine(root, name);
                if (File.Exists(direct)) return direct;
                string wpf = Path.Combine(root, "WPF", name);
                if (File.Exists(wpf)) return wpf;
            }
            return null;
        }

        private string BuildCompilerArguments(string output, string revitApi, string revitApiUi, string presentationCore, string windowsBase, string webExtensions, List<string> sources)
        {
            var b = new StringBuilder();
            b.Append("/nologo /target:library /platform:x64 /optimize+ ");
            b.Append("/out:").Append(Quote(output)).Append(' ');
            b.Append("/reference:").Append(Quote(revitApi)).Append(' ');
            b.Append("/reference:").Append(Quote(revitApiUi)).Append(' ');
            b.Append("/reference:").Append(Quote(presentationCore)).Append(' ');
            b.Append("/reference:").Append(Quote(windowsBase)).Append(' ');
            b.Append("/reference:").Append(Quote(webExtensions)).Append(' ');
            foreach (string source in sources) b.Append(Quote(source)).Append(' ');
            return b.ToString();
        }

        private string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        private string EscapeXml(string value)
        {
            return value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&apos;");
        }

        private void CreateRibbonIcons(string assetsDir)
        {
            CreateIcon(Path.Combine(assetsDir, "AlignGridEnds_16.png"), 16);
            CreateIcon(Path.Combine(assetsDir, "AlignGridEnds_32.png"), 32);
        }

        private void CreateIcon(string path, int size)
        {
            int s = size * 4;
            using (var bitmap = new Bitmap(s, s))
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.Clear(Color.Transparent);
                float k = s / 32F;
                using (var linePen = new Pen(Color.FromArgb(36, 82, 126), 1.8F * k))
                using (var accentPen = new Pen(Color.FromArgb(31, 144, 255), 2.0F * k))
                using (var accentBrush = new SolidBrush(Color.FromArgb(31, 144, 255)))
                {
                    float[] xs = new float[] { 8F * k, 16F * k, 24F * k };
                    foreach (float x in xs)
                    {
                        graphics.DrawLine(linePen, x, 7F * k, x, 25F * k);
                        graphics.DrawEllipse(accentPen, x - 2.6F * k, 2.4F * k, 5.2F * k, 5.2F * k);
                        graphics.FillEllipse(accentBrush, x - 2F * k, 23F * k, 4F * k, 4F * k);
                    }
                    graphics.DrawLine(accentPen, 5F * k, 25F * k, 27F * k, 25F * k);
                }
                using (var small = new Bitmap(bitmap, new Size(size, size)))
                    small.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }
        }

        private void SetProgress(int value, string text)
        {
            BeginInvoke((Action)delegate
            {
                _progress.Value = Math.Max(_progress.Minimum, Math.Min(_progress.Maximum, value));
                _status.Text = text;
            });
        }

        private sealed class UpdateManifest
        {
            public bool enabled { get; set; }
            public string version { get; set; }
            public string revit { get; set; }
            public List<UpdateFile> files { get; set; }
            public string updater_url { get; set; }
        }

        private sealed class UpdateFile
        {
            public string name { get; set; }
            public string url { get; set; }
        }
    }
}
