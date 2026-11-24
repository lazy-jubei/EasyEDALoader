using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Win32;

namespace EasyEDALoaderSetup
{
    internal sealed class Installation
    {
        internal string DisplayName;
        internal string AltiumVersion;
        internal string ExtensionsRoot;
        public override string ToString() => DisplayName;
    }

    internal static class InstallerEngine
    {
        internal const string ExtensionName = "EasyEDA-Loader";
        internal const string RegistryName = "ExtensionsRegistry.xml";
        internal static readonly string Version = Assembly.GetExecutingAssembly().GetName().Version.ToString();
        private static readonly string[] CommonFiles =
        {
            "EasyEDA-Loader.dll", "EasyEDA-Loader.Ins", "EasyEDA-Loader.rcs",
            "Newtonsoft.Json.dll", "EasyEDA-Loader.target.json"
        };
        private static readonly string[] OptionalFiles =
        {
            "EasyEDA-Loader.dll.config", "Newtonsoft.Json.xml", "LICENSE", "THIRD-PARTY-NOTICES.md"
        };

        internal static IEnumerable<Installation> Discover()
        {
            string altiumData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Altium");
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                using (var baseKey = RegistryKey.OpenBaseKey(hive, view))
                using (var uninstall = baseKey.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall"))
                {
                    if (uninstall == null) continue;
                    foreach (string name in uninstall.GetSubKeyNames())
                    {
                        if (!Regex.IsMatch(name, @"^Altium Designer \{[0-9a-f-]+\}$", RegexOptions.IgnoreCase)) continue;
                        using (var entry = uninstall.OpenSubKey(name))
                        {
                            if (entry == null) continue;
                            string location = entry.GetValue("InstallLocation") as string;
                            string version = entry.GetValue("DisplayVersion") as string;
                            System.Version parsed;
                            if (string.IsNullOrWhiteSpace(location) || !System.Version.TryParse(version, out parsed)
                                || (parsed.Major != 17 && parsed.Major != 26)) continue;
                            string root = Path.Combine(altiumData, name, "Extensions");
                            if (!File.Exists(Path.Combine(location, "System", "Altium.SDK.dll"))
                                || !File.Exists(Path.Combine(root, RegistryName)) || !seen.Add(root)) continue;
                            yield return new Installation
                            {
                                DisplayName = $"Altium Designer {version} — {location}",
                                AltiumVersion = parsed.Major.ToString(CultureInfo.InvariantCulture),
                                ExtensionsRoot = root
                            };
                        }
                    }
                }
            }
        }

        internal static string Install(Installation installation)
        {
            using (var installLock = new Mutex(false, @"Global\EasyEDALoaderSetup"))
            {
                try
                {
                    if (!installLock.WaitOne(0)) throw new InvalidOperationException("Another EasyEDA Loader setup is running.");
                }
                catch (AbandonedMutexException) { /* A previous setup ended before releasing the lock. */ }

                try
                {
                    if (IsAltiumRunning("X2") || IsAltiumRunning("DXP"))
                        throw new InvalidOperationException("Save your work and close Altium Designer before installing.");
                    return Deploy(installation);
                }
                finally { installLock.ReleaseMutex(); }
            }
        }

        private static bool IsAltiumRunning(string name)
        {
            var processes = Process.GetProcessesByName(name);
            foreach (var process in processes) process.Dispose();
            return processes.Length > 0;
        }

        private static byte[] ReadEntry(ZipArchiveEntry entry)
        {
            using (var stream = entry.Open())
            using (var memory = new MemoryStream())
            {
                stream.CopyTo(memory);
                return memory.ToArray();
            }
        }

        private static Dictionary<string, byte[]> Payload(string altiumVersion)
        {
            bool legacy = altiumVersion == "17";
            if (!legacy && altiumVersion != "26") throw new ArgumentException("Unsupported Altium Designer version.");
            string folder = legacy ? "dist-ad17/" : "dist/";
            var required = legacy ? CommonFiles : CommonFiles.Concat(new[] { "EasyEDA-Loader.deps.json" }).ToArray();
            var allowed = new HashSet<string>(required.Concat(OptionalFiles), StringComparer.OrdinalIgnoreCase);
            var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip"))
            using (var memory = new MemoryStream())
            using (var hash = SHA256.Create())
            {
                if (stream == null) throw new InvalidDataException("The installation payload is missing.");
                stream.CopyTo(memory);
                if (BitConverter.ToString(hash.ComputeHash(memory.ToArray())).Replace("-", "") != PayloadManifest.Sha256)
                    throw new InvalidDataException("The installation payload failed the integrity check.");
                memory.Position = 0;
                using (var archive = new ZipArchive(memory, ZipArchiveMode.Read))
                foreach (var entry in archive.Entries)
                {
                    string path = entry.FullName.Replace('\\', '/');
                    if (!path.StartsWith(folder, StringComparison.Ordinal) || path == folder) continue;
                    string file = path.Substring(folder.Length);
                    if (!allowed.Contains(file) || files.ContainsKey(file))
                        throw new InvalidDataException("Unexpected or duplicate installation payload file: " + path);
                    files.Add(file, ReadEntry(entry));
                }
            }

            if (required.Any(file => !files.ContainsKey(file)))
                throw new InvalidDataException("Incomplete AD" + altiumVersion + " installation payload.");

            var manifest = new JavaScriptSerializer().Deserialize<Dictionary<string, string>>(
                Encoding.UTF8.GetString(files["EasyEDA-Loader.target.json"]).TrimStart('\uFEFF'));
            string target, framework, architecture;
            if (manifest == null
                || !manifest.TryGetValue("altiumVersion", out target) || target != altiumVersion
                || !manifest.TryGetValue("framework", out framework) || framework != (legacy ? "net48" : "net8.0-windows")
                || !manifest.TryGetValue("architecture", out architecture) || architecture != (legacy ? "x86" : "x64"))
                throw new InvalidDataException("Installer payload target does not match AD" + altiumVersion + ".");

            string ins = Encoding.UTF8.GetString(files["EasyEDA-Loader.Ins"]);
            if (!Regex.IsMatch(ins, @"(?m)^\s*Version\s*=\s*'Version " + Regex.Escape(Version) + @"'\s*$"))
                throw new InvalidDataException("The plugin manifest version does not match the installer.");
            return files;
        }

        private static void UpdateRegistration(XElement root, string destination, string altiumVersion)
        {
            var entries = root.Elements("Item").Where(item => (string)item.Attribute("HRID") == ExtensionName).ToArray();
            if (entries.Length > 1) throw new InvalidDataException("Duplicate EasyEDA Loader records in the Altium extensions registry.");
            var registration = entries.SingleOrDefault();
            if (registration == null)
            {
                string date = DateTime.Today.ToOADate().ToString("F7", CultureInfo.InvariantCulture);
                registration = new XElement("Item", new XAttribute("HRID", ExtensionName),
                    new XAttribute("Guid", "8035C261-E5FE-403B-A9B5-9ABFFB6E0EF5"),
                    new XElement("Status", "0"), new XElement("VaultGuid"),
                    new XElement("CreatedBy", "EasyEDALoader contributors"),
                    new XElement("CategoryGuid", "793A1F67-0B22-4E01-A5DE-3176A1E8C60D"),
                    new XElement("CategoryName"), new XElement("ReadMe"), new XElement("Help"),
                    new XElement("Requirements"), new XElement("Title", ExtensionName),
                    new XElement("ShortDescription", ExtensionName),
                    new XElement("LongDescription", "Imports EasyEDA components into Altium Designer."),
                    new XElement("SmallImage"), new XElement("LargeImage"),
                    new XElement("ReleasedDate", date), new XElement("ReleaseNotes"), new XElement("DateInstalled", date));
                root.Add(registration);
            }

            registration.SetElementValue("Path", destination);
            registration.SetElementValue("Version", Version);
            registration.SetElementValue("VersionGuid", "7042BC82-F870-462D-86AF-B158AC75C490");
            var platform = registration.Element("PlatformVersions");
            if (platform == null)
            {
                platform = new XElement("PlatformVersions");
                registration.Add(platform);
            }

            var minimums = altiumVersion == "17"
                ? new[] { "1.0.5.5", "10.0.5.5", "0.0.0.0", "0.0.0.0" }
                : new[] { "1.0.16.41", "1.0.16.41", "0.0.0.0", "0.0.0.0" };
            var names = new[] { "DXP", "EDP", "MaxDXP", "MaxEDP" };
            for (int i = 0; i < names.Length; i++)
            {
                var node = platform.Element(names[i]);
                if (node == null)
                {
                    node = new XElement(names[i]);
                    platform.Add(node);
                }
                node.SetAttributeValue("BuildNumber", minimums[i]);
            }
        }

        internal static string Deploy(Installation installation)
        {
            string root = installation.ExtensionsRoot;
            string registry = Path.Combine(root, RegistryName);
            string destination = Path.Combine(root, ExtensionName);
            var files = Payload(installation.AltiumVersion);
            var document = XDocument.Load(registry);
            if (document.Root == null || document.Root.Name != "Extensions")
                throw new InvalidDataException("Invalid Altium extensions registry.");
            UpdateRegistration(document.Root, destination, installation.AltiumVersion);

            // Altium scans the Extensions tree. Keep backup copies and staging outside it.
            string backup = Path.Combine(Path.GetDirectoryName(root), "EasyEDALoader-Backups",
                DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            string previous = Path.Combine(backup, ExtensionName);
            string stage = Path.Combine(backup, "new-plugin");
            string newRegistry = Path.Combine(backup, "new-registry.xml");
            Directory.CreateDirectory(stage);
            bool backedUp = false;
            bool installed = false;
            try
            {
                File.Copy(registry, Path.Combine(backup, RegistryName));
                foreach (var file in files) File.WriteAllBytes(Path.Combine(stage, file.Key), file.Value);
                var assembly = AssemblyName.GetAssemblyName(Path.Combine(stage, "EasyEDA-Loader.dll"));
                if (assembly.Version.ToString() != Version)
                    throw new InvalidDataException("The plugin assembly version does not match the installer.");
                var encoding = new UTF8Encoding(false);
                using (var writer = XmlWriter.Create(newRegistry, new XmlWriterSettings { Encoding = encoding, Indent = true }))
                    document.Save(writer);

                if (Directory.Exists(destination))
                {
                    Directory.Move(destination, previous);
                    backedUp = true;
                }
                Directory.Move(stage, destination);
                installed = true;
                File.Replace(newRegistry, registry, null);
            }
            catch
            {
                try
                {
                    if (installed) Directory.Delete(destination, true);
                    if (backedUp) Directory.Move(previous, destination);
                }
                catch (Exception rollbackFailure)
                {
                    throw new IOException("Installation could not restore the old plugin. The backup is at " + backup, rollbackFailure);
                }
                throw;
            }
            finally
            {
                if (Directory.Exists(stage)) Directory.Delete(stage, true);
                File.Delete(newRegistry);
            }
            return backup;
        }
    }
}
