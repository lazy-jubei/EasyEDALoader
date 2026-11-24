using DXP;
using SCH;
using System;
using System.IO;
using System.Collections.Generic;
using System.Runtime.InteropServices;
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
                throw new InvalidOperationException($"Altium could not place the component. The exported library is at {schLibraryPath}.");
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

            Directory.CreateDirectory(dialog.OutputDirectory);
            LibraryOutput.RememberDirectory(dialog.OutputDirectory);
            ImportLog.Reset(dialog.OutputDirectory);

            var importer = new ComponentLibraryImporter(AltiumApi.GlobalVars.Client);
            var failures = new List<string>();
            (LibraryOutput Output, string Reference) last = default;
            try
            {
                foreach (var selection in dialog.SelectedComponents)
                {
                    try
                    {
                        last = importer.Import(selection, dialog.OutputDirectory, !dialog.CloseDocuments);
                    }
                    catch (Exception ex)
                    {
                        string part = selection.PartInfo.Part;
                        RuntimeDiagnostics.Error($"Import {part}", ex);
                        failures.Add($"{part}: {ex.GetBaseException().Message}");
                    }
                }
            }
            finally
            {
                if (currentDoc != null) AltiumApi.GlobalVars.Client.ShowDocument(currentDoc);
            }

            if (dialog.PlaceInSchematic && last.Output != null)
                PlaceComponent(last.Output.SchematicPath, last.Reference);

            if (ImportLog.ErrorCount > 0)
                failures.Add($"Import details were written to {ImportLog.LogPath}");
            if (failures.Count > 0)
                MessageBox.Show(string.Join(Environment.NewLine, failures), "EasyEDA Loader",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
