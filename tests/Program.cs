using System.Net.Http;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Net;
using System.Net.Sockets;
using System.IO.Compression;
using System.Text;
using EasyEDA_Loader;

internal static class Program
{
    private static int checks;
    [STAThread]
    public static int Main(string[] args)
    {
        string altium = args[0];
        AssemblyLoadContext.Default.Resolving += (_, name) => {
            foreach (var directory in new[] { altium, Path.Combine(altium, "System"), Path.Combine(altium, "System/DotNet/DevExpress.Wpf") }) {
                var file = Path.Combine(directory, name.Name + ".dll");
                if (File.Exists(file)) return AssemblyLoadContext.Default.LoadFromAssemblyPath(file);
            }
            return null;
        };
        try { Run().GetAwaiter().GetResult(); Console.WriteLine($"PASS: {checks} regression checks"); return 0; }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
    private static async Task Run()
    {
        CheckOutputLayout();
        Check(EasyedaApi.GetImageUri("https://images.example/part.png").AbsoluteUri == "https://images.example/part.png", "Absolute thumbnail URL was corrupted.");
        Check(EasyedaApi.GetImageUri("//images.example/part.png").Scheme == "https", "Protocol-relative thumbnail.");
        Check(EasyedaApi.GetImageUri("/part.png").AbsoluteUri == "https://image.lceda.cn/part.png", "Relative thumbnail.");
        try { EasyedaApi.GetImageUri("file:///etc/passwd"); throw new Exception("Non-HTTP image URL accepted."); } catch (ArgumentException) { checks++; }
        var circle = EeSymbolCircle.FromString("C~10.5~20.25~3~#000000~1~0~none~circle1~0");
        Check(circle.CenterX == 10.5 && circle.CenterY == 20.25 && circle.Radius == 3 && circle.Id == "circle1", "Circle fields shifted by shape prefix.");
        var coordinates = EePoint.ListFromString("  1  2\t3 -4 ");
        Check(coordinates.Count == 2 && coordinates[1].X == EeShape.ConvertToMM(3) &&
            coordinates[1].Y == EeShape.ConvertToMM(-4), "Footprint point lists did not handle repeated whitespace.");
        try { EePoint.ListFromString("1 2 3"); throw new Exception("Incomplete footprint coordinate accepted."); }
        catch (FormatException) { checks++; }
        var ellipseArc = EeFootprintArc.ArcPath.FromString("M 10, 20  a 4  8 30 0 1  6, -3");
        Check(ellipseArc.EndPoint.X == EeShape.ConvertToMM(16) && ellipseArc.EndPoint.Y == EeShape.ConvertToMM(17) &&
            ellipseArc.Size.Width == EeShape.ConvertToMM(4) && ellipseArc.Size.Height == EeShape.ConvertToMM(8) &&
            ellipseArc.RotationAngle == 30, "Footprint parser lost ellipse geometry or relative endpoint.");
        var arcContext = new EeFootprintContext
        {
            Box = new BoundingBoxMm(),
            Layers = new EeFootprintLayers
            {
                Layers = new Dictionary<string, EeFootprintLayer>
                {
                    ["3"] = new EeFootprintLayer { Color = "#123456" }
                }
            }
        };
        var ellipseShape = new EeFootprintArc { Path = ellipseArc, LayerId = "3", StrokeWidth = 0.1 };
        var ellipsePreview = (System.Windows.Shapes.Path)ellipseShape.AddToCanvas(null, arcContext).Single();
        var ellipseFigure = ((System.Windows.Media.PathGeometry)ellipsePreview.Data).Figures.Single();
        var ellipseSegment = (System.Windows.Media.ArcSegment)ellipseFigure.Segments.Single();
        Check(ellipseSegment.Size == ellipseArc.Size && ellipseSegment.Point == ellipseArc.EndPoint &&
            ellipseSegment.RotationAngle == ellipseArc.RotationAngle, "Footprint preview distorted an elliptical arc.");
        var lineShape = new EeFootprintArc
        {
            Path = EeFootprintArc.ArcPath.FromString("M 1,2 A 0 8 30 0 1 4,5"),
            LayerId = "3",
            StrokeWidth = 0.1
        };
        var linePreview = (System.Windows.Shapes.Path)lineShape.AddToCanvas(null, arcContext).Single();
        Check(((System.Windows.Media.PathGeometry)linePreview.Data).Figures.Single().Segments.Single()
            is System.Windows.Media.LineSegment, "A zero-radius footprint arc should be a line.");
        var circleArc = EeFootprintArc.ArcPath.FromString("M 0,0 A 5 5 0 0 1 10,0");
        Check(Math.Abs(circleArc.Radius - EeShape.ConvertToMM(5)) < 1e-9 &&
            Math.Abs(circleArc.EndAngle - circleArc.StartAngle - 180) < 1e-9,
            "A circular footprint arc no longer converts to a native arc.");
        var shapes = new List<EeSymbolShape>();
        for (int side = 0; side < 4; side++) for (int pin = 0; pin < 3; pin++) shapes.Add(new EeSymbolPin {
            Settings = new EeSymbolPinSettings { PosX = pin, PosY = pin, SpicePinNumber = $"{side}:{pin}" },
            Name = new EeSymbolPinName { Rotation = side == 0 || side == 3 ? 270 : 0, TextAnchor = side == 0 || side == 2 ? "end" : "start", Text = "pin" }
        });
        var (rect, pins) = SymbolDrawing.LayoutPins(shapes);
        foreach (int side in new[] { 0, 3 }) Check(pins.Where(p => p.Designator.StartsWith(side+":")).Average(p=>p.X) == rect.Width / 2, "Top/bottom pins are not centered.");
        foreach (int side in new[] { 1, 2 }) Check(pins.Where(p => p.Designator.StartsWith(side+":")).Average(p=>p.Y) == rect.Height / 2, "Side pins are not centered.");
        var model = new EeFootprint3dModel();
        foreach (double z in new[] { 2.5, -2.5 }) {
            var ctx = new EeFootprintContext { RawModelTask = Task.FromResult(Encoding.UTF8.GetBytes($"v 0 0 {z.ToString(System.Globalization.CultureInfo.InvariantCulture)}\nv 1 1 4\n")) };
            Check(await model.GetZOffsetFromOrigin(ctx) == -z, "OBJ offset must subtract the minimum height.");
        }
        var spacedObj = new EeFootprintContext
        {
            RawModelTask = Task.FromResult(Encoding.UTF8.GetBytes("  v\t0  0 -1.25\n vn 1 2 -8\n v 0 0 -Infinity\n"))
        };
        Check(await model.GetZOffsetFromOrigin(spacedObj) == 1.25,
            "OBJ vertex parsing ignored tabs or accepted a non-finite height.");
        int before = Directory.GetFiles(Path.GetTempPath(), "easyeda-*.step").Length;
        Check(!model.AddToComponent(null, new EeFootprintContext { ModelTask = Task.FromResult(Encoding.UTF8.GetBytes("invalid STEP")), RawModelTask = Task.FromResult(Encoding.UTF8.GetBytes("v 0 0 0")), Exception = _ => false }), "Import failure not reported.");
        Check(Directory.GetFiles(Path.GetTempPath(), "easyeda-*.step").Length == before, "Failed 3D import leaked its temporary file.");
        using var handler = new FakeHandler(); using var client = new HttpClient(handler);
        var api = new EasyedaApi(client);
        handler.Json = "{\"success\":true,\"result\":{\"productList\":[{\"mpn\":\"Part\",\"number\":\"C1\",\"device_info\":{\"description\":\"Real description\",\"symbol_info\":{},\"footprint_info\":{\"model_3d\":null}}}]}}";
        var products = await api.SearchProductInfoAsync("C1 &another=query");
        Check(handler.Uri.Query.Contains("%26another%3Dquery"), "Search query was not escaped.");
        Check(products.Single().Description == "Real description", "Lowercase description was ignored.");
        Check(products.Single().HasSymbol && products.Single().HasFootprint && !products.Single().Has3d, "Missing attributes or JSON null break availability flags.");
        Check(handler.Content.Disposed, "HTTP response was not disposed.");
        handler.Json = """
            {"result":{"productList":[null,{},{"mpn":"Part","number":"C2","device_info":{
              "description":{"code":"unsupported"}, "Description":"Fallback", "symbol_info":false,
              "footprint_info":"footprint-id", "attributes":{"Nested":{"data":1},"Scalar":12.5,
              "3D Model Transform":"10,20,30,0,not-an-angle,0,0,0,0"}
            }}]}}
            """;
        var sparseProduct = (await api.SearchProductInfoAsync("C2")).Single();
        Check(sparseProduct.Part == "C2" && sparseProduct.Description == "Fallback" && sparseProduct.HasFootprint &&
            !sparseProduct.Has3d && !sparseProduct.HasSymbol, "Sparse product records broke search result parsing.");
        Check(sparseProduct.Info.Parameters["Scalar"] == "12.5" && !sparseProduct.Info.Parameters.ContainsKey("Nested"),
            "Search metadata did not preserve scalar attributes.");
        Check(sparseProduct.Info.Size == null && sparseProduct.Info.Rotation == null && sparseProduct.Info.Offset == null,
            "An invalid 3D transform left partial coordinates.");
        handler.Status = HttpStatusCode.InternalServerError;
        try { await api.SearchProductInfoAsync("C1"); throw new Exception("HTTP failure was hidden as no results."); } catch (HttpRequestException) { checks++; }
        handler.Status = HttpStatusCode.OK;
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        try { await api.LoadModelAsync("model", cancellation.Token); throw new Exception("Cancellation was swallowed."); } catch (OperationCanceledException) { checks++; }
        await Compression();
    }

    private static void CheckOutputLayout()
    {
        string root = Path.Combine(Path.GetTempPath(), "easyeda-layout-" + Guid.NewGuid().ToString("N"));
        try
        {
            var first = new LibraryOutput(root, "RP2040", " c2040 ");
            Check(first.DirectoryPath == Path.Combine(root, "RP2040-easy"), "The component folder must use the part name.");
            Check(Path.GetFileName(first.SchematicPath) == "RP2040-C2040.SchLib" &&
                Path.GetFileName(first.FootprintPath) == "RP2040-C2040.PcbLib", "The library pair must share one part-specific basename.");
            Directory.CreateDirectory(first.DirectoryPath);
            File.WriteAllText(first.SchematicPath, "symbol");
            Check(!first.HasPair, "A single file must not be treated as an exported pair.");
            File.WriteAllText(first.FootprintPath, "footprint");
            Check(first.HasPair, "A saved pair was not detected.");
            var second = new LibraryOutput(root, "RP2040", "C2041");
            Check(second.DirectoryPath == Path.Combine(root, "RP2040-C2041-easy"), "A same-name import must have its own folder.");
            Check(new LibraryOutput(root, "RP2040", "C2040").DirectoryPath == first.DirectoryPath,
                "Repeat import must reuse its existing folder.");
            var sanitized = new LibraryOutput(root, "CON.variant:/", "C123");
            Check(Path.GetFileName(sanitized.DirectoryPath) == "_CON.variant__-easy", "Unsafe Windows filename or device name was accepted.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
    private static async Task Compression()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = Task.Run(async () => {
            using var connection = await listener.AcceptTcpClientAsync(); using var stream = connection.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
            while (!string.IsNullOrEmpty(await reader.ReadLineAsync())) { }
            using var buffer = new MemoryStream();
            using (var compressor = new BrotliStream(buffer, CompressionLevel.Optimal, true)) compressor.Write(Encoding.UTF8.GetBytes("compressed response"));
            byte[] body = buffer.ToArray();
            byte[] header = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Encoding: br\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(header); await stream.WriteAsync(body);
        });
        try {
            using var client = (HttpClient)typeof(EasyedaApi).GetMethod("CreateHttpClient", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
            Check(await client.GetStringAsync($"http://127.0.0.1:{port}/") == "compressed response", "Brotli response was not decompressed.");
            await server;
        } finally { listener.Stop(); }
    }
    private sealed class TrackedContent : StringContent
    {
        public bool Disposed;
        public TrackedContent(string json) : base(json, Encoding.UTF8, "application/json") { }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }
    private sealed class FakeHandler : HttpMessageHandler
    {
        public string Json; public HttpStatusCode Status = HttpStatusCode.OK; public Uri Uri; public TrackedContent Content;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) {
            token.ThrowIfCancellationRequested(); Uri = request.RequestUri; Content = new TrackedContent(Json ?? "{}");
            return Task.FromResult(new HttpResponseMessage(Status) { Content = Content });
        }
    }
}
