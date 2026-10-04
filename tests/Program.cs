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
        Check(EasyedaApi.GetImageUri("https://images.example/part.png").AbsoluteUri == "https://images.example/part.png", "Absolute thumbnail URL was corrupted.");
        Check(EasyedaApi.GetImageUri("//images.example/part.png").Scheme == "https", "Protocol-relative thumbnail.");
        Check(EasyedaApi.GetImageUri("/part.png").AbsoluteUri == "https://image.lceda.cn/part.png", "Relative thumbnail.");
        try { EasyedaApi.GetImageUri("file:///etc/passwd"); throw new Exception("Non-HTTP image URL accepted."); } catch (ArgumentException) { checks++; }
        var circle = EeSymbolCircle.FromString("C~10.5~20.25~3~#000000~1~0~none~circle1~0");
        Check(circle.CenterX == 10.5 && circle.CenterY == 20.25 && circle.Radius == 3 && circle.Id == "circle1", "Circle fields shifted by shape prefix.");
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
        handler.Status = HttpStatusCode.InternalServerError;
        try { await api.SearchProductInfoAsync("C1"); throw new Exception("HTTP failure was hidden as no results."); } catch (HttpRequestException) { checks++; }
        handler.Status = HttpStatusCode.OK;
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        try { await api.LoadModelAsync("model", cancellation.Token); throw new Exception("Cancellation was swallowed."); } catch (OperationCanceledException) { checks++; }
        await Compression();
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
