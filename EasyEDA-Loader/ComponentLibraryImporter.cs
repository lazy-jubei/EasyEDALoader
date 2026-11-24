using DXP;
using PCB;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace EasyEDA_Loader
{
    internal sealed class ComponentLibraryImporter
    {
        private readonly IClient client;
        private readonly EasyedaApi api = new EasyedaApi();

        internal ComponentLibraryImporter(IClient client)
        {
            this.client = client;
        }

        internal (LibraryOutput Output, string Reference) Import(ComponentSelection selection, string outputDirectory,
            bool keepOpen)
        {
            var symbol = selection.Root?.Component?.Symbol;
            string symbolName = symbol?.Head?.Parameters?.Name;
            var packageDetail = selection.Root?.Component?.PackageDetail;
            var footprint = packageDetail?.Footprint;
            string package = footprint?.Head?.Parameters?.Package;
            if (string.IsNullOrWhiteSpace(symbolName) || symbol.Shapes == null || symbol.Shapes.Count == 0)
                throw new InvalidDataException("This part has no named schematic symbol.");
            if (string.IsNullOrWhiteSpace(package) || footprint.Shapes == null || footprint.Shapes.Count == 0)
                throw new InvalidDataException("This part has no named PCB footprint.");

            string name = string.IsNullOrWhiteSpace(selection.PartInfo.Name) ? symbolName : selection.PartInfo.Name;
            var output = new LibraryOutput(outputDirectory, name, selection.PartInfo.Part);

            if (!output.HasPair)
            {
                if (Directory.Exists(output.DirectoryPath))
                    throw new IOException($"The component folder already exists without a complete library pair: {output.DirectoryPath}");
                WriteNewPair(output, outputDirectory, symbol, symbolName, footprint, package,
                    packageDetail.Title, selection.PartInfo.Info);
            }
            if (keepOpen)
            {
                client.ShowDocument(Open("PcbLib", output.FootprintPath));
                client.ShowDocument(Open("SchLib", output.SchematicPath));
            }

            return (output, symbolName);
        }

        private IServerDocument Open(string kind, string path)
        {
            return client.OpenDocument(kind, path)
                ?? throw new InvalidOperationException($"Altium could not open {path}.");
        }

        private void WriteNewPair(LibraryOutput output, string outputDirectory, SymbolData symbol, string symbolName,
            FootprintData footprint, string package, string packageDescription, EasyedaApi.ProductInfo product)
        {
            // Keep the native writes on the destination volume so both files become
            // visible together. Link the symbol to the final footprint location.
            string staging = Path.Combine(outputDirectory, ".easyeda-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            try
            {
                string pcbPath = Path.Combine(staging, Path.GetFileName(output.FootprintPath));
                string schPath = Path.Combine(staging, Path.GetFileName(output.SchematicPath));
                string pattern = WriteFootprint(pcbPath, footprint, package, packageDescription);
                WriteSymbol(schPath, output.FootprintPath, symbol, symbolName, pattern, product);
                string pair = Path.Combine(staging, "pair");
                Directory.CreateDirectory(pair);
                File.Move(pcbPath, Path.Combine(pair, Path.GetFileName(pcbPath)));
                File.Move(schPath, Path.Combine(pair, Path.GetFileName(schPath)));
                Directory.Move(pair, output.DirectoryPath);
            }
            finally
            {
                try
                {
                    if (Directory.Exists(staging)) Directory.Delete(staging, true);
                }
                catch (IOException ex) { RuntimeDiagnostics.Error("Clean staging folder", ex); }
                catch (UnauthorizedAccessException ex) { RuntimeDiagnostics.Error("Clean staging folder", ex); }
            }
        }

        private string WriteFootprint(string path, FootprintData footprint, string package, string description)
        {
            var doc = Open("PcbLib", path);
            try
            {
                client.ShowDocument(doc);
                var server = AltiumApi.GlobalVars.PCBServer;
                var library = server.GetCurrentPCBLibrary()
                    ?? throw new InvalidOperationException("Altium could not access the new PCB library.");
                var component = EEPCB.CreateFootprintInLib(library, package, description)
                    ?? throw new InvalidOperationException("Altium could not create the footprint.");

                // Avoid waiting on an async continuation on Altium's UI thread.
                using var context = new CancellationTokenSource();
                var model = footprint.GetModel();
                var modelTask = model != null ? Task.Run(() => api.LoadModelAsync(model.Uuid, context.Token)) : null;
                var rawModelTask = model != null ? Task.Run(() => api.LoadRawModelAsync(model.Uuid, context.Token)) : null;
                var settings = new EeFootprintContext
                {
                    Box = footprint.BoundingBox,
                    Layers = footprint.Layers,
                    CancelToken = context.Token,
                    Exception = ex =>
                    {
                        ImportLog.Error($"footprint '{package}'", ex);
                        return false;
                    },
                    ModelTask = modelTask,
                    RawModelTask = rawModelTask
                };
                server.PreProcess();
                try
                {
                    if (!footprint.AddToComponent(component, settings))
                        throw new InvalidDataException("Altium could not import the complete footprint.");
                }
                finally
                {
                    server.PostProcess();
                    context.Cancel();
                }
                string pattern = component.GetState_Pattern();
                if (string.IsNullOrWhiteSpace(pattern) || library.GetComponentByName(pattern) == null)
                    throw new InvalidOperationException("Altium did not register the imported footprint in the PCB library.");
                Save(doc, "PcbLib", path);
                return pattern;
            }
            finally { CloseStagedDocument(doc, path); }
        }

        private void WriteSymbol(string path, string pcbPath, SymbolData symbol, string name, string pattern,
            EasyedaApi.ProductInfo product)
        {
            var doc = Open("SchLib", path);
            try
            {
                client.ShowDocument(doc);
                var library = EESCH.GetCurrentSchLibrary()
                    ?? throw new InvalidOperationException("Altium could not access the new schematic library.");
                var component = EESCH.CreateComponent(name, product?.Description ?? name,
                    symbol.Head.Parameters.Pre ?? "U")
                    ?? throw new InvalidOperationException("Altium could not create the schematic symbol.");
                var process = client.GetProcessControl();
                process.PreProcess(doc, "");
                try
                {
                    SymbolDrawing.CreateComponent(library, component, pcbPath, pattern, symbol);
                    if (product?.Parameters != null)
                    {
                        foreach (var parameter in product.Parameters)
                            EESCH.AddParameter(component, parameter.Key, parameter.Value);
                    }
                }
                finally { process.PostProcess(doc, ""); }
                library.SetState_Current_SchComponent(component);
                library.GraphicallyInvalidate();
                Save(doc, "SchLib", path);
            }
            finally { CloseStagedDocument(doc, path); }
        }

        private static void Save(IServerDocument document, string kind, string path)
        {
            if (!document.DoFileSave(kind) || !LibraryOutput.IsSaved(path))
                throw new IOException($"Altium could not save {path}.");
        }

        private void CloseStagedDocument(IServerDocument document, string path)
        {
            // Only the newly created staging documents can be discarded here.
            document.SetModified(false);
            client.CloseDocument(document);
            if (client.IsDocumentOpen(path))
                throw new IOException($"Altium kept the staging document open: {path}");
        }
    }
}
