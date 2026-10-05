using System;
using System.IO;

namespace EasyEDA_Loader
{
    internal static class RuntimeDiagnostics
    {
        internal static void Error(string operation, Exception error)
        {
            try
            {
                string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EasyEDALoader");
                Directory.CreateDirectory(root);
                File.AppendAllText(Path.Combine(root, "runtime-errors.log"), $"{DateTime.UtcNow:u} {operation}\n{error}\n\n");
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
