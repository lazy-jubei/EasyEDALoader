using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;

using EasyEDA_Loader;
using Microsoft.Win32;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;
using System.Windows.Threading;
using System.Collections.Generic;

namespace Standalone
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private bool busy;
        private bool closed;
        protected EasyedaApi Api;

        public ComponentInfo? Component;
        public EeFootprint3dModel? Model;
        public ModelVisual3D? RawModel;
        public CancellationTokenSource cts;

        public CanvasZoomPanHelper _footprintHelper;
        public CanvasZoomPanHelper _symbolHelper;

        public MainWindow()
        {
            cts = new CancellationTokenSource();
            Api = new EasyedaApi();
            InitializeComponent();

            _footprintHelper = new CanvasZoomPanHelper(FootprintCanvas);

            FootprintCanvasView.ScrollChanged += (s, e) =>
            {
                if (e.ViewportWidthChange != 0 || e.ViewportHeightChange != 0)
                    _footprintHelper.FitToBoundingBox();
            };

            _symbolHelper = new CanvasZoomPanHelper(SymbolCanvas);

            SymbolCanvasView.ScrollChanged += (s, e) =>
            {
                if (e.ViewportWidthChange != 0 || e.ViewportHeightChange != 0)
                    _symbolHelper.FitToBoundingBox();
            };

            var cam = ModelView.Camera as ProjectionCamera;
            if (cam != null)
            {
                cam.Position = new Point3D(0, 0, 30);       // Camera above the origin
                cam.LookDirection = new Vector3D(0, 0, -30); // Looking down at origin
                cam.UpDirection = new Vector3D(0, 1, 0);      // Y-axis as up

                ModelView.Camera = cam;

                // Optionally call ResetCamera() to update internals
                ModelView.ResetCamera();
            }
        }

        public static void SaveModelToFile(EeFootprint3dModel model, byte[] fileData)
        {
            var saveFileDialog = new SaveFileDialog
            {
                Title = "Save File As",
                Filter = "SwSTEP 2.0|*.step|All Files|*.*",
                FileName = $"{model.Name}.step",
                DefaultExt = "step"
            };

            bool? result = saveFileDialog.ShowDialog();
            if (result == true)
            {
                string selectedPath = saveFileDialog.FileName;
                File.WriteAllBytes(selectedPath, fileData);
            }
        }
        public static void SaveRawModelToFile(EeFootprint3dModel model, byte[] fileData)
        {
            var saveFileDialog = new SaveFileDialog
            {
                Title = "Save File As",
                Filter = "Obj|*.obj|All Files|*.*",
                FileName = $"{model.Name}.obj",
                DefaultExt = "obj"
            };

            bool? result = saveFileDialog.ShowDialog();
            if (result == true)
            {
                string selectedPath = saveFileDialog.FileName;
                File.WriteAllBytes(selectedPath, fileData);
            }
        }

        private async Task LoadPartAsync(string partName, EasyedaApi.ProductInfo? info = null)
        {
            if (RawModel != null)
                ModelView.Children.Remove(RawModel);

            FootprintCanvas.Children.Clear();

            var token = cts.Token;
            var root = await Api.GetComponentJsonAsync(partName.Trim(), token);
            token.ThrowIfCancellationRequested();
            Component = root?.Component ?? throw new InvalidOperationException("No component data was returned.");
            if (info == null)
            {
                var parts = await Api.SearchProductInfoAsync(partName, token);
                token.ThrowIfCancellationRequested();
                info = parts.FirstOrDefault(p => string.Equals(p.Part, partName.Trim(), StringComparison.OrdinalIgnoreCase))?.Info;
            }
            DetailsView.ItemsSource = null;
            if (info != null) PopulateParameters(info);
            SymbolCanvas.Children.Clear();
            if (Component.Symbol == null) throw new InvalidOperationException("This part has no schematic symbol.");

            SymbolDrawing.DrawComponent(SymbolCanvas, Component.Symbol.Shapes);
            await SymbolCanvas.Dispatcher.InvokeAsync(() =>
            {
                _symbolHelper.FitToBoundingBox();
            }, DispatcherPriority.Loaded);

            var eeFootprint = Component.PackageDetail?.Footprint;

            Model = eeFootprint?.GetModel();

            ModelButton.IsEnabled = Model != null;
            ObjButton.IsEnabled = Model != null;

            Thumbnail.Source = null;
            if (!string.IsNullOrWhiteSpace(Component.Thumb))
            {
                try { Thumbnail.Source = await Api.LoadPngAsync(Component.Thumb, token); }
                catch (OperationCanceledException) { throw; }
                catch (Exception) { Thumbnail.Source = null; }
            }
            token.ThrowIfCancellationRequested();
            if (eeFootprint == null) return;

            EeFootprintContext ctx = new()
            {
                Box = eeFootprint.BoundingBox,
                Layers = eeFootprint.Layers,
                CancelToken = token,
                Exception = null,
            };

            byte[]? rawModelData = null;
            if(Model != null)
            {
                rawModelData = await Api.LoadRawModelAsync(Model.Uuid, token);
                token.ThrowIfCancellationRequested();
                ctx.RawModelTask = Task.FromResult(rawModelData);

                using var stream = new MemoryStream(rawModelData);
                var importer = new ObjReader();

                Model3D model = importer.Read(stream);
                if (model != null)
                {
                    RawModel = new ModelVisual3D { Content = model };
                    var transformGroup = new Transform3DGroup();
                    var rotationX = new AxisAngleRotation3D(new Vector3D(1, 0, 0), Model.Rotation.X);
                    var rotationY = new AxisAngleRotation3D(new Vector3D(0, 1, 0), Model.Rotation.Y);
                    var rotationZ = new AxisAngleRotation3D(new Vector3D(0, 0, 1), Model.Rotation.Z);
                    var rotateTransformX = new RotateTransform3D(rotationX);
                    var rotateTransformY = new RotateTransform3D(rotationY);
                    var rotateTransformZ = new RotateTransform3D(rotationZ);
                    transformGroup.Children.Add(rotateTransformX);
                    transformGroup.Children.Add(rotateTransformY);
                    transformGroup.Children.Add(rotateTransformZ);
                    RawModel.Transform = transformGroup;
                    ModelView.Children.Add(RawModel);
                }
            }

            eeFootprint.DrawToCanvas(FootprintCanvas, ctx);
            await FootprintCanvas.Dispatcher.InvokeAsync(() =>
            {
                _footprintHelper.FitToBoundingBox();
            }, DispatcherPriority.Loaded);
        }

        private async Task RunBusyAsync(Func<Task> action)
        {
            if (busy || closed) return;
            busy = true;
            SearchButton.IsEnabled = LoadButton.IsEnabled = ModelButton.IsEnabled = ObjButton.IsEnabled = false;
            try { await action(); }
            catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
            catch (Exception ex) { if (!closed) MessageBox.Show(this, ex.Message, "EasyEDA Loader", MessageBoxButton.OK, MessageBoxImage.Error); }
            finally
            {
                busy = false;
                if (closed) cts.Dispose();
                else
                {
                    SearchButton.IsEnabled = LoadButton.IsEnabled = true;
                    ModelButton.IsEnabled = ObjButton.IsEnabled = Model != null;
                }
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            closed = true;
            cts.Cancel();
            if (!busy) cts.Dispose();
            base.OnClosed(e);
        }

        private async void LoadButton_Click(object sender, RoutedEventArgs e)
            => await RunBusyAsync(() => LoadPartAsync(PartId.Text));

        private async void ModelButton_Click(object sender, RoutedEventArgs e)
        {
            var model = Model;
            if (model == null) return;
            await RunBusyAsync(async () => {
                var token = cts.Token;
                var data = await Api.LoadModelAsync(model.Uuid, token);
                token.ThrowIfCancellationRequested();
                SaveModelToFile(model, data);
            });
        }

        private async void ObjModelButton_Click(object sender, RoutedEventArgs e)
        {
            var model = Model;
            if (model == null) return;
            await RunBusyAsync(async () => {
                var token = cts.Token;
                var data = await Api.LoadRawModelAsync(model.Uuid, token);
                token.ThrowIfCancellationRequested();
                SaveRawModelToFile(model, data);
            });
        }

        private void PopulateSearchBox(List<EasyedaApi.PartInfo> parts)
        {
            SearchBox.ItemsSource = parts?.ToList();
            NameColumn.Width = Double.NaN;
            PartColumn.Width = Double.NaN;
            DescColumn.Width = Double.NaN;
        }

        private async void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            string partName = PartId.Text;
            await RunBusyAsync(async () => {
                var token = cts.Token;
                var parts = await Api.SearchProductInfoAsync(partName, token);
                token.ThrowIfCancellationRequested();
                PopulateSearchBox(parts);
            });
        }

        private void PopulateParameters(EasyedaApi.ProductInfo productInfo)
        {
            DetailsView.ItemsSource = productInfo.Parameters;
            KeyColumn.Width = Double.NaN;
            ValueColumn.Width = Double.NaN;
        }

        private void SearchBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (SearchBox.SelectedItem is EasyedaApi.PartInfo selectedItem)
            {
                PopulateParameters(selectedItem.Info);
            }
        }

        private async void SearchBox_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (SearchBox.SelectedItem is EasyedaApi.PartInfo selectedItem)
            {
                await RunBusyAsync(() => LoadPartAsync(selectedItem.Part, selectedItem.Info));
            }
        }
    }
}
