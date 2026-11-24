using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Xml.Linq;

namespace EasyEDALoaderSetup
{
    internal sealed class SetupForm : Form
    {
        private readonly ComboBox targets = new ComboBox();
        private readonly Button install = new Button();
        private readonly TextBox status = new TextBox();

        internal SetupForm()
        {
            Text = "EasyEDA Loader Setup";
            ClientSize = new Size(600, 260);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Font;
            Font = new Font("Segoe UI", 9);

            Controls.Add(new Label { Text = "EasyEDA Loader " + InstallerEngine.Version, AutoSize = true, Left = 20, Top = 18 });
            Controls.Add(new Label { Text = "Altium Designer installation", AutoSize = true, Left = 20, Top = 53 });
            targets.SetBounds(20, 76, 560, 28);
            targets.DropDownStyle = ComboBoxStyle.DropDownList;
            targets.Items.AddRange(InstallerEngine.Discover().Cast<object>().ToArray());
            if (targets.Items.Count != 0) targets.SelectedIndex = 0;
            Controls.Add(targets);

            status.SetBounds(20, 118, 560, 86);
            status.Multiline = true;
            status.ReadOnly = true;
            status.ScrollBars = ScrollBars.Vertical;
            status.Text = targets.Items.Count == 0
                ? "No registered Altium Designer 17 or 26 installation was found."
                : "Close Altium Designer, then choose Install / Update.";
            Controls.Add(status);

            install.Text = "Install / Update";
            install.SetBounds(20, 218, 130, 30);
            install.Enabled = targets.Items.Count != 0;
            install.Click += InstallClicked;
            Controls.Add(install);

            var close = new Button { Text = "Close", DialogResult = DialogResult.Cancel };
            close.SetBounds(480, 218, 100, 30);
            Controls.Add(close);
            CancelButton = close;
        }

        private void InstallClicked(object sender, EventArgs e)
        {
            try
            {
                var installation = (Installation)targets.SelectedItem;
                string backup = InstallerEngine.Install(installation);
                status.Text = "Installed. Reopen Altium Designer to use EasyEDA Loader." +
                    Environment.NewLine + "Backup: " + backup;
            }
            catch (UnauthorizedAccessException)
            {
                status.Text = "Windows denied write access. Close Setup, then reopen it using Run as administrator.";
            }
            catch (Exception failure)
            {
                status.Text = failure.Message;
            }
        }
    }

    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "--self-test")
                return SelfTest(Path.GetFullPath(args[1]));
            if (args.Length != 0) return 2;

            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new SetupForm());
                return 0;
            }
            catch (Exception failure)
            {
                MessageBox.Show(failure.Message, "EasyEDA Loader Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        private static int SelfTest(string folder)
        {
            if (Directory.Exists(folder) || File.Exists(folder)) return 2;
            Directory.CreateDirectory(folder);
            string results = Path.Combine(folder, "result.txt");
            try
            {
                foreach (string version in new[] { "17", "26" })
                    TestInstallation(folder, version);
                File.WriteAllText(results, "PASS: Both payloads, clean install, upgrade, backup, and rollback.");
                return 0;
            }
            catch (Exception failure)
            {
                File.WriteAllText(results, failure.ToString());
                return 1;
            }
        }

        private static void TestInstallation(string folder, string version)
        {
            string extensions = Path.Combine(folder, "AD" + version, "Extensions");
            string registry = Path.Combine(extensions, InstallerEngine.RegistryName);
            string plugin = Path.Combine(extensions, InstallerEngine.ExtensionName);
            Directory.CreateDirectory(extensions);
            File.WriteAllText(registry, "<Extensions><Item HRID='Other'><Path>untouched</Path></Item></Extensions>");
            var installation = new Installation { AltiumVersion = version, ExtensionsRoot = extensions };
            InstallerEngine.Deploy(installation);
            string stale = Path.Combine(plugin, "stale.dll");
            File.WriteAllText(stale, "Previous version");
            string backup = InstallerEngine.Deploy(installation);
            if (File.Exists(stale) || !File.Exists(Path.Combine(backup, InstallerEngine.ExtensionName, "stale.dll")))
                throw new InvalidOperationException("Upgrade did not isolate the previous plugin.");

            byte[] originalRegistry = File.ReadAllBytes(registry);
            if (originalRegistry.Take(3).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }))
                throw new InvalidOperationException("Registry contains a UTF-8 byte order mark.");
            var document = XDocument.Load(registry);
            var item = document.Root.Elements("Item").Single(node => (string)node.Attribute("HRID") == InstallerEngine.ExtensionName);
            string minimumEdp = version == "17" ? "10.0.5.5" : "1.0.16.41";
            double date;
            if (document.Root.Elements("Item").Single(node => (string)node.Attribute("HRID") == "Other").Element("Path").Value != "untouched"
                || item.Element("Version").Value != InstallerEngine.Version
                || item.Element("Path").Value != plugin
                || (string)item.Element("PlatformVersions").Element("EDP").Attribute("BuildNumber") != minimumEdp
                || !double.TryParse(item.Element("ReleasedDate").Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out date))
                throw new InvalidOperationException("Unexpected registry contents.");

            File.WriteAllText(stale, "Recent file");
            bool failed = false;
            using (File.Open(registry, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                try { InstallerEngine.Deploy(installation); }
                catch (IOException) { failed = true; }
                catch (UnauthorizedAccessException) { failed = true; }
            }
            if (!failed || !File.Exists(stale) || !originalRegistry.SequenceEqual(File.ReadAllBytes(registry)))
                throw new InvalidOperationException("Locked registry did not preserve the previous installation.");
        }
    }
}
