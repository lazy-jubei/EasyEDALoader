using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Windows;

namespace Standalone
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // Preview drawing uses SDK types; resolve them from the installation
            // instead of redistributing Altium and DevExpress assemblies.
            AssemblyLoadContext.Default.Resolving += ResolveAltiumAssembly;
            base.OnStartup(e);
        }

        private static Assembly? ResolveAltiumAssembly(AssemblyLoadContext context, AssemblyName name)
        {
            if (name.Name == null || !(name.Name.StartsWith("Altium.", StringComparison.Ordinal) ||
                name.Name.StartsWith("DevExpress.", StringComparison.Ordinal))) return null;
            string altium = Environment.GetEnvironmentVariable("ALTIUM_INSTALL_DIR")
                ?? @"C:\Program Files\Altium\AD26";
            foreach (string directory in new[] { Path.Combine(altium, "System"), Path.Combine(altium, "System", "DotNet", "DevExpress.Wpf") })
            {
                string file = Path.GetFullPath(Path.Combine(directory, name.Name + ".dll"));
                if (File.Exists(file)) return context.LoadFromAssemblyPath(file);
            }
            return null;
        }
    }
}
