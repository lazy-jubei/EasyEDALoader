using System;
using System.IO;
using System.Linq;

namespace EasyEDA_Loader
{
    public sealed class LibraryOutput
    {
        public string DirectoryPath { get; }
        public string PartNumber { get; }
        public string FileName { get; }
        public string SchematicPath => Path.Combine(DirectoryPath, FileName + ".SchLib");
        public string FootprintPath => Path.Combine(DirectoryPath, FileName + ".PcbLib");
        public bool HasPair => IsSaved(SchematicPath) && IsSaved(FootprintPath);

        public LibraryOutput(string outputDirectory, string partName, string partNumber)
        {
            string id = partNumber?.Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(id) || id.Length < 2 || id[0] != 'C')
                throw new ArgumentException("An LCSC part number such as C2040 is required.", nameof(partNumber));
            for (int i = 1; i < id.Length; i++)
            {
                if (id[i] < '0' || id[i] > '9')
                    throw new ArgumentException("An LCSC part number such as C2040 is required.", nameof(partNumber));
            }

            PartNumber = id;
            string root = NormalizeDirectory(outputDirectory);
            string name = CleanName(partName, id);
            FileName = name == id ? id : name + "-" + id;
            string preferred = Path.Combine(root, name + "-easy");
            string alternate = Path.Combine(root, FileName + "-easy");
            // The SKU in the file name identifies existing imports even when multiple
            // LCSC parts have the same manufacturer part number.
            DirectoryPath = Directory.Exists(alternate) ? alternate : preferred;
            if (!Directory.Exists(alternate) && Directory.Exists(preferred) &&
                !File.Exists(Path.Combine(preferred, FileName + ".SchLib")) &&
                !File.Exists(Path.Combine(preferred, FileName + ".PcbLib")))
                DirectoryPath = alternate;
        }

        internal static bool IsSaved(string path) => File.Exists(path) && new FileInfo(path).Length > 0;

        private static string CleanName(string partName, string fallback)
        {
            const string invalid = "<>:\"/\\|?*";
            string name = new string((partName ?? "").Trim()
                .Select(c => c < ' ' || invalid.Contains(c) ? '_' : c).ToArray());
            if (name.Length > 72)
                name = name.Substring(0, char.IsHighSurrogate(name[71]) ? 71 : 72);
            name = name.Trim(' ', '.');
            if (name.Length == 0) return fallback;

            string stem = name.Split('.')[0].TrimEnd().ToUpperInvariant();
            if (stem == "CON" || stem == "PRN" || stem == "AUX" || stem == "NUL" ||
                (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) ||
                    stem.StartsWith("LPT", StringComparison.Ordinal)) && "123456789¹²³".Contains(stem[3])))
                name = "_" + name;
            return name;
        }

        public static string NormalizeDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Choose an output directory.");
            string fullPath = Path.GetFullPath(path.Trim());
            if (File.Exists(fullPath))
                throw new IOException("The output path points to a file. Choose a directory.");
            return fullPath;
        }

        private static string PreferencePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EasyEDALoader", "output-directory.txt");

        internal static string PreferredDirectory()
        {
            string configured = Environment.GetEnvironmentVariable("EASYEDA_LIBRARY_DIR");
            if (!string.IsNullOrWhiteSpace(configured))
                return configured;
            try
            {
                if (File.Exists(PreferencePath))
                {
                    string saved = File.ReadAllText(PreferencePath).Trim();
                    if (saved.Length > 0) return saved;
                }
            }
            catch (IOException ex) { RuntimeDiagnostics.Error("Read output directory", ex); }
            catch (UnauthorizedAccessException ex) { RuntimeDiagnostics.Error("Read output directory", ex); }

            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "AltiumEE");
        }

        internal static void RememberDirectory(string directory)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(PreferencePath));
                File.WriteAllText(PreferencePath, directory);
            }
            catch (IOException ex) { RuntimeDiagnostics.Error("Save output directory", ex); }
            catch (UnauthorizedAccessException ex) { RuntimeDiagnostics.Error("Save output directory", ex); }
        }
    }
}
