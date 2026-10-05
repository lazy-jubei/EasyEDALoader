using DXP;
using PCB;
using SCH;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace EasyEDA_Loader
{
    [ClassInterface(ClassInterfaceType.AutoDispatch)]
    public class EasyEDALoaderModule : ServerModule
    {
        private bool noGUIMode;

        public EasyEDALoaderModule(IClient argClient)
          : base(argClient, "EasyEDA-Loader")
        {
            noGUIMode = argClient.ProductInfo().SupportsUIFeature("NoGUI", false);
        }

        protected override IServerDocument NewDocumentInstance(string argKind, string argFileName) => (IServerDocument)null;

        protected override void InitializeCommands()
        {
            RegisterCommand("EasyEDARun", new CommandProc(Run));
#if ALTIUM17
            RegisterCommand("ManufacturerPartSearch", new CommandProc(RunManufacturerPartSearch), "Manufacturer Part Search Error");
#endif
        }

#if ALTIUM17
        private void RunManufacturerPartSearch(IServerDocumentView context, ref string parameters)
        {
            if (noGUIMode) throw new InvalidOperationException("Manufacturer Part Search requires Altium's graphical interface.");
            var currentDocument = AltiumApi.GlobalVars.Client.GetCurrentView()?.GetOwnerDocument();
            try
            {
                bool canPlace = string.Equals(currentDocument?.GetKind(), "SCH", StringComparison.OrdinalIgnoreCase);
                var window = new PartSearch.PartSearchWindow(canPlace);
                if (DialogHost.Show(window) != true || window.SelectedPart == null || window.SelectedModel == null) return;
                var imported = PartSearch.SupplierLibraryImporter.Import(window.SelectedPart, window.SelectedOffer, window.SelectedModel);
                if (window.PlaceInSchematic)
                {
                    if (!canPlace) throw new InvalidOperationException("Open a schematic before placing a component.");
                    AltiumApi.GlobalVars.Client.ShowDocument(currentDocument);
                    var manager = EDP.Utils.LoadIntegratedLibraryManager()
                        ?? throw new InvalidOperationException("Altium's library manager is unavailable.");
                    // The library manager requires explicit placement coordinates.
                    string placement = "Location.X=0|Location.Y=0|Orientation=0|PartID=" +
                        imported.PartId.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    if (!manager.PlaceLibraryComponent(imported.Reference, imported.LibraryPath, placement))
                        throw new InvalidOperationException("Altium could not place the component. The imported component remains in ManufacturerParts.schlib.");
                    AltiumApi.GlobalVars.SCHServer.GetCurrentSchDocument()?.GraphicallyInvalidate();
                }
            }
            finally { if (currentDocument != null) AltiumApi.GlobalVars.Client.ShowDocument(currentDocument); }
        }
#endif

        private void RegisterCommand(string argCommandId, CommandProc commandProc, string errorTitle = "EasyEDA Loader Error") => ((DXP.CommandLauncher)CommandLauncher).RegisterCommand(argCommandId, (CommandProc)((IServerDocumentView view, ref string parameters) =>
        {
            try
            {
                commandProc(view, ref parameters);
            }
            catch (Exception ex)
            {
                RuntimeDiagnostics.Error(argCommandId, ex);
                if (noGUIMode)
                {
                    throw;
                }
                else
                {
                    int num = (int)MessageBox.Show(ex.GetBaseException().Message, errorTitle, MessageBoxButtons.OK, MessageBoxIcon.Hand);
                }
            }
        }));

        private static void PlaceComponent(string schLibraryPath, string partName)
        {
            var currentSheet = AltiumApi.GlobalVars.SCHServer.GetCurrentSchDocument();
            if (currentSheet == null)
                throw new InvalidOperationException("Must be in a schematic document before placing a component.");

            // Let Altium register the placement and its undo operation together.
            var manager = EDP.Utils.LoadIntegratedLibraryManager()
                ?? throw new InvalidOperationException("Altium's library manager is unavailable.");
            if (!manager.PlaceLibraryComponent(partName, schLibraryPath,
                "Location.X=0|Location.Y=0|Orientation=0|PartID=1"))
                throw new InvalidOperationException("Altium could not place the component. It remains available in EasyEDA.schlib.");
            currentSheet.GraphicallyInvalidate();
        }

        private void Run(
          IServerDocumentView argContext,
          ref string argParameters)
        {
            var currentDoc = AltiumApi.GlobalVars.Client.GetCurrentView()?.GetOwnerDocument();
            Dialog dialog = new Dialog();
            DialogResult result = dialog.ShowDialog();
            if (result != DialogResult.OK || dialog.SelectedComponents.Count == 0)
                return;

            if (dialog.PlaceInSchematic && !string.Equals(currentDoc?.GetKind(), "SCH", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("Must be in a schematic document before running", "EasyEDA Loader Error", MessageBoxButtons.OK, MessageBoxIcon.Hand);
                return;
            }

            using var ctx = new CancellationTokenSource();
            var api = new EasyedaApi();
            ImportLog.Reset();

            string documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string libraryPath = Environment.GetEnvironmentVariable("EASYEDA_LIBRARY_DIR")
                ?? Path.Combine(documentsPath, "AltiumEE");
            Directory.CreateDirectory(libraryPath);
            string pcbLibraryPath = Path.Combine(libraryPath, "EasyEDA.pcblib");
            string schLibraryPath = Path.Combine(libraryPath, "EasyEDA.schlib");

            IServerDocument pcbDocument = null;
            IPCB_Library pcbLib = null;
            IServerDocument schDocument = null;
            try
            {
                if (dialog.SelectedComponents.Exists(selection => selection.IncludeFootprint))
                {
                    pcbDocument = AltiumApi.GlobalVars.Client.OpenDocument("PcbLib", pcbLibraryPath)
                        ?? throw new InvalidOperationException("Could not open the PCB library.");
                    AltiumApi.GlobalVars.Client.ShowDocument(pcbDocument);
                    pcbLib = AltiumApi.GlobalVars.PCBServer.GetCurrentPCBLibrary()
                        ?? throw new InvalidOperationException("Could not access the PCB library.");
                }

                schDocument = AltiumApi.GlobalVars.Client.OpenDocument("SchLib", schLibraryPath)
                    ?? throw new InvalidOperationException("Could not open the schematic library.");
                AltiumApi.GlobalVars.Client.ShowDocument(schDocument);
                var schLib = EESCH.GetCurrentSchLibrary()
                    ?? throw new InvalidOperationException("Could not access the schematic library.");

                // Process each selected component
                foreach (var selection in dialog.SelectedComponents)
                {
                    try
                    {
                        var root = selection.Root;
                        var ee_footprint = root?.Component?.PackageDetail?.Footprint;
                        var ee_symbol = root?.Component?.Symbol
                            ?? throw new InvalidOperationException("This part has no schematic symbol.");
                        string package = ee_footprint?.Head?.Parameters?.Package;
                        if (selection.IncludeFootprint && (ee_footprint == null || string.IsNullOrWhiteSpace(package)))
                            throw new InvalidOperationException("This part has no named PCB footprint.");
                        EeFootprint3dModel model = selection.IncludeFootprint && selection.Include3dModel
                            ? ee_footprint?.GetModel() : null;

                        // Prefetch model if we can
                        Task<byte[]> modelTask = model != null ? Task.Run(() => api.LoadModelAsync(model.Uuid, ctx.Token)) : null;
                        Task<byte[]> rawModelTask = model != null ? Task.Run(() => api.LoadRawModelAsync(model.Uuid, ctx.Token)) : null;

                        // Get product info (use cached from search if available)
                        EasyedaApi.ProductInfo productInfo = selection.PartInfo.Info;

                        // Create PCB footprint if requested
                        if (selection.IncludeFootprint)
                        {
                            AltiumApi.GlobalVars.Client.ShowDocument(pcbDocument);
                            var libComp = pcbLib.GetComponentByName(package);
                            bool createdFootprint = false;
                            if (libComp == null)
                            {
                                libComp = EEPCB.CreateFootprintInLib(package, root.Component.PackageDetail.Title);
                                createdFootprint = libComp != null;
                            }

                            if (createdFootprint)
                            {
                                AltiumApi.GlobalVars.PCBServer.PreProcess();
                                try
                                {
                                    var footprintContext = new EeFootprintContext
                                    {
                                        Box = ee_footprint.BoundingBox,
                                        Layers = ee_footprint.Layers,
                                        CancelToken = ctx.Token,
                                        Exception = (Exception ex) =>
                                        {
                                            ImportLog.Error($"footprint '{package}'", ex);
                                            return true;
                                        },
                                        ModelTask = modelTask,
                                        RawModelTask = rawModelTask,
                                    };
                                    ee_footprint.AddToComponent(libComp, footprintContext);
                                }
                                finally { AltiumApi.GlobalVars.PCBServer.PostProcess(); }
                                pcbDocument.DoFileSave("PcbLib");
                            }
                        }

                        // Create schematic symbol
                        AltiumApi.GlobalVars.Client.ShowDocument(schDocument);
                        string partName = ee_symbol.Head.Parameters.Name;
                        string description = productInfo?.Description ?? partName;

                        var existingComponent = schLib.GetState_SchComponentByLibRef(partName);
                        if (existingComponent == null)
                        {
                            var component = EESCH.CreateComponent(partName, description, ee_symbol.Head.Parameters.Pre);
                            if (schLib != null && component != null)
                            {
                                var processControl = AltiumApi.GlobalVars.Client.GetProcessControl();
                                processControl.PreProcess(schDocument, "");
                                try
                                {
                                    SymbolDrawing.CreateComponent(schLib, component, pcbLibraryPath, selection.IncludeFootprint ? package : null, ee_symbol);

                                    if (productInfo?.Parameters != null)
                                    {
                                        foreach (var kvp in productInfo.Parameters)
                                        {
                                            EESCH.AddParameter(component, kvp.Key, kvp.Value);
                                        }
                                    }
                                }
                                finally { processControl.PostProcess(schDocument, ""); }
                                schLib.SetState_Current_SchComponent(component);
                                schLib.GraphicallyInvalidate();
                                schDocument.DoFileSave("SchLib");
                            }
                        }

                        // Place component in schematic if requested (only the last one)
                        if (dialog.PlaceInSchematic && selection == dialog.SelectedComponents[dialog.SelectedComponents.Count - 1])
                        {
                            // Return to the original document before placing
                            AltiumApi.GlobalVars.Client.ShowDocument(currentDoc);
                            PlaceComponent(schLibraryPath, partName);
                        }
                    }
                    catch (Exception ex)
                    {
                        RuntimeDiagnostics.Error($"Import {selection.PartInfo.Name}", ex);
                        MessageBox.Show($"Failed to process component {selection.PartInfo.Name}: {ex.Message}", "EasyEDA Loader Error", MessageBoxButtons.OK, MessageBoxIcon.Hand);
                    }
                }
            }
            finally
            {
                if (currentDoc != null) AltiumApi.GlobalVars.Client.ShowDocument(currentDoc);
                if (dialog.CloseDocuments)
                {
                    if (pcbDocument != null) AltiumApi.GlobalVars.Client.CloseDocument(pcbDocument);
                    if (schDocument != null) AltiumApi.GlobalVars.Client.CloseDocument(schDocument);
                }
            }

            if (ImportLog.ErrorCount > 0)
            {
                MessageBox.Show(
                    $"{ImportLog.ErrorCount} primitive(s) failed to import. Details were written to:\n{ImportLog.LogPath}",
                    "EasyEDA Loader", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
