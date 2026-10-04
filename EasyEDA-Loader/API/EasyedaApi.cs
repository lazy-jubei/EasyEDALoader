using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace EasyEDA_Loader
{
    public class EasyedaApi
    {
        private const string Version = "6.4.19.5";
        private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/142.0.0.0 Safari/537.36";
        private static readonly HttpClient SharedClient = CreateHttpClient();
        private readonly HttpClient HttpClient;

        public EasyedaApi(HttpClient httpClient = null)
        {
            HttpClient = httpClient ?? SharedClient;
        }

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient(new HttpClientHandler
            {
                AutomaticDecompression = System.Net.DecompressionMethods.GZip |
                    System.Net.DecompressionMethods.Deflate | System.Net.DecompressionMethods.Brotli
            }) { Timeout = TimeSpan.FromSeconds(30) };
            client.DefaultRequestHeaders.Add("Accept", "application/json, text/javascript, */*; q=0.01");
            client.DefaultRequestHeaders.Add("User-Agent", UserAgent);
            return client;
        }

        public static Uri GetImageUri(string imageUrl)
        {
            if (string.IsNullOrWhiteSpace(imageUrl))
                throw new ArgumentException("The thumbnail URL is empty.", nameof(imageUrl));
            if (imageUrl.StartsWith("//", StringComparison.Ordinal))
                imageUrl = "https:" + imageUrl;
            else if (!Uri.TryCreate(imageUrl, UriKind.Absolute, out _))
                imageUrl = "https://image.lceda.cn/" + imageUrl.TrimStart('/');
            var uri = new Uri(imageUrl, UriKind.Absolute);
            if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
                throw new ArgumentException("The thumbnail must use HTTP or HTTPS.", nameof(imageUrl));
            return uri;
        }

        private void LogResponse(string content)
        {
            try
            {
                var jsonObj = JToken.Parse(content);
                string prettyJson = jsonObj.ToString(Formatting.Indented);
                Debug.WriteLine($"[API] Response Content (Pretty JSON):\n{prettyJson}");
                Console.WriteLine($"[API] Response Content (Pretty JSON):\n{prettyJson}");
            }
            catch
            {
                Debug.WriteLine($"[API] Response Content: {content}");
                Console.WriteLine($"[API] Response Content: {content}");
            }
        }

        public async Task<Root> GetComponentJsonAsync(string lcscId, CancellationToken cancellationToken)
        {
            string url = $"https://easyeda.com/api/products/{lcscId}/components?version={Version}";
            Debug.WriteLine($"[API] GET Request: {url}");
            Console.WriteLine($"[API] GET Request: {url}");

            try
            {
                using var response = await HttpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
                Debug.WriteLine($"[API] Response Status: {(int)response.StatusCode} {response.StatusCode}");
                Console.WriteLine($"[API] Response Status: {(int)response.StatusCode} {response.StatusCode}");
                
                response.EnsureSuccessStatusCode();
                var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                
                Debug.WriteLine($"[API] Response Length: {content.Length} characters");
                Console.WriteLine($"[API] Response Length: {content.Length} characters");
                LogResponse(content);
                
                var result = JsonConvert.DeserializeObject<Root>(content);
                Debug.WriteLine($"[API] Deserialized successfully");
                Console.WriteLine($"[API] Deserialized successfully");
                
                return result;
            }
            catch (OperationCanceledException cancel)
            {
                Debug.WriteLine($"[API] Download was cancelled: {cancel.Message}");
                Console.WriteLine($"Download was cancelled: {cancel.Message}");
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[API] Error: {ex.Message}");
                Debug.WriteLine($"[API] Stack Trace: {ex.StackTrace}");
                Console.WriteLine($"[API] Error: {ex.Message}");
                Console.WriteLine($"[API] Stack Trace: {ex.StackTrace}");
                throw;
            }
        }

        public async Task<BitmapImage> LoadPngAsync(string imageUrl, CancellationToken cancellationToken)
        {
            var fullUrl = GetImageUri(imageUrl);
            Debug.WriteLine($"[API] GET Request (Image): {fullUrl}");
            Console.WriteLine($"[API] GET Request (Image): {fullUrl}");

            try
            {
                using var res = await HttpClient.GetAsync(fullUrl, cancellationToken).ConfigureAwait(false);
                Debug.WriteLine($"[API] Response Status: {(int)res.StatusCode} {res.StatusCode}");
                Console.WriteLine($"[API] Response Status: {(int)res.StatusCode} {res.StatusCode}");
                
                res.EnsureSuccessStatusCode();
                var imageData = await res.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                
                Debug.WriteLine($"[API] Image Data Length: {imageData.Length} bytes");
                Console.WriteLine($"[API] Image Data Length: {imageData.Length} bytes");

                var bitmap = new BitmapImage();
                using var stream = new MemoryStream(imageData);

                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();
                
                Debug.WriteLine($"[API] Image loaded successfully");
                Console.WriteLine($"[API] Image loaded successfully");
                return bitmap;
            }
            catch (OperationCanceledException cancel)
            {
                Debug.WriteLine($"[API] Download was cancelled: {cancel.Message}");
                Console.WriteLine($"Download was cancelled: {cancel.Message}");
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[API] Error loading image: {ex.Message}");
                Console.WriteLine($"[API] Error loading image: {ex.Message}");
                throw;
            }
        }

        public async Task<byte[]> LoadModelAsync(string modelUuid, CancellationToken cancellationToken)
        {
            string url = $"https://modules.easyeda.com/qAxj6KHrDKw4blvCG8QJPs7Y/{modelUuid}";
            Debug.WriteLine($"[API] GET Request (Model): {url}");
            Console.WriteLine($"[API] GET Request (Model): {url}");

            try
            {
                using var res = await HttpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
                Debug.WriteLine($"[API] Response Status: {(int)res.StatusCode} {res.StatusCode}");
                Console.WriteLine($"[API] Response Status: {(int)res.StatusCode} {res.StatusCode}");
                
                res.EnsureSuccessStatusCode();
                var data = await res.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                
                Debug.WriteLine($"[API] Model Data Length: {data.Length} bytes");
                Console.WriteLine($"[API] Model Data Length: {data.Length} bytes");
                return data;
            }
            catch (OperationCanceledException cancel)
            {
                Debug.WriteLine($"[API] Download was cancelled: {cancel.Message}");
                Console.WriteLine($"Download was cancelled: {cancel.Message}");
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[API] Error loading model: {ex.Message}");
                Console.WriteLine($"[API] Error loading model: {ex.Message}");
                throw;
            }
        }

        public async Task<byte[]> LoadRawModelAsync(string modelUuid, CancellationToken cancellationToken)
        {
            string url = $"https://modules.easyeda.com/3dmodel/{modelUuid}";
            Debug.WriteLine($"[API] GET Request (Raw Model): {url}");
            Console.WriteLine($"[API] GET Request (Raw Model): {url}");

            try
            {
                using var res = await HttpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
                Debug.WriteLine($"[API] Response Status: {(int)res.StatusCode} {res.StatusCode}");
                Console.WriteLine($"[API] Response Status: {(int)res.StatusCode} {res.StatusCode}");
                
                res.EnsureSuccessStatusCode();
                var data = await res.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                
                Debug.WriteLine($"[API] Raw Model Data Length: {data.Length} bytes");
                Console.WriteLine($"[API] Raw Model Data Length: {data.Length} bytes");
                return data;
            }
            catch (OperationCanceledException cancel)
            {
                Debug.WriteLine($"[API] Download was cancelled: {cancel.Message}");
                Console.WriteLine($"Download was cancelled: {cancel.Message}");
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[API] Error loading raw model: {ex.Message}");
                Console.WriteLine($"[API] Error loading raw model: {ex.Message}");
                throw;
            }
        }

        public class PartInfo
        {
            public string Name { get; set; }
            public string Part { get; set; }
            public string Description { get; set; }
            public ProductInfo Info { get; set; }
            public bool HasSymbol { get; set; }
            public bool Has3d { get; set; }
            public bool HasFootprint { get; set; }
        }

        public class ProductInfo
        {
            public string Description { get; set; }
            public Vec3 Size { get; set; }
            public Vec3 Rotation { get; set; }
            public Vec3 Offset { get; set; }
            public Dictionary<string, string> Parameters { get; set; }
        }
        public async Task<ProductInfo> GetProductInfoAsync(string search, string uuid, CancellationToken cancellationToken = default)
        {
            string url = $"https://pro.easyeda.com/api/v2/devices/search";
            Debug.WriteLine($"[API] POST Request: {url}");
            Console.WriteLine($"[API] POST Request: {url}");
            
            var formData = new Dictionary<string, string>
            {
                { "page", "1" },
                { "pageSize", "1" },
                { "uid", uuid },
                { "path", uuid },
                { "wd", search.ToLowerInvariant() },
                { "returnListStyle", "classifyarr" }
            };
            
            Debug.WriteLine($"[API] Request Data: {string.Join(", ", formData.Select(kvp => $"{kvp.Key}={kvp.Value}"))}");
            Console.WriteLine($"[API] Request Data: {string.Join(", ", formData.Select(kvp => $"{kvp.Key}={kvp.Value}"))}");
            
            using var content = new FormUrlEncodedContent(formData);
            
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = content;
            request.Headers.Add("Referer", "https://pro.easyeda.com/editor");
            request.Headers.Add("Origin", "https://pro.easyeda.com");
            request.Headers.Add("X-Requested-With", "XMLHttpRequest");
            request.Headers.Add("sec-fetch-dest", "empty");
            request.Headers.Add("sec-fetch-mode", "cors");
            request.Headers.Add("sec-fetch-site", "same-origin");

            try
            {
                using var response = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                Debug.WriteLine($"[API] Response Status: {(int)response.StatusCode} {response.StatusCode}");
                Console.WriteLine($"[API] Response Status: {(int)response.StatusCode} {response.StatusCode}");
                
                response.EnsureSuccessStatusCode();
                string result = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                
                Debug.WriteLine($"[API] Response Length: {result.Length} characters");
                Console.WriteLine($"[API] Response Length: {result.Length} characters");
                LogResponse(result);
                
                JObject obj = JObject.Parse(result);
                JObject obj_info = (JObject)obj["result"]["lists"]["lcsc"][0];
                var attributes = obj_info["attributes"].ToObject<Dictionary<string, string>>();
                return ProductFromAttributes(attributes, obj_info["description"].ToString());
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[API] Error: {ex.Message}");
                Console.WriteLine($"[API] Error: {ex.Message}");
                throw;
            }
        }

        static public ProductInfo ProductFromAttributes(Dictionary<string, string> attributes, string description)
        {
            Vec3 size = null;
            Vec3 rotation = null;
            Vec3 offset = null;

            if (attributes != null && attributes.ContainsKey("3D Model Transform"))
            {
                try
                {
                    var transformInfo = attributes["3D Model Transform"].Split(',');
                    if (transformInfo.Length >= 9)
                    {
                        size = new Vec3
                        {
                            X = EeShape.ConvertToMM(double.Parse(transformInfo[0], System.Globalization.CultureInfo.InvariantCulture)) / 10,
                            Y = EeShape.ConvertToMM(double.Parse(transformInfo[1], System.Globalization.CultureInfo.InvariantCulture)) / 10,
                            Z = EeShape.ConvertToMM(double.Parse(transformInfo[2], System.Globalization.CultureInfo.InvariantCulture)) / 10,
                        };
                        rotation = new Vec3
                        {
                            X = double.Parse(transformInfo[3], System.Globalization.CultureInfo.InvariantCulture),
                            Y = double.Parse(transformInfo[4], System.Globalization.CultureInfo.InvariantCulture),
                            Z = double.Parse(transformInfo[5], System.Globalization.CultureInfo.InvariantCulture),
                        };
                        offset = new Vec3
                        {
                            X = EeShape.ConvertToMM(double.Parse(transformInfo[6], System.Globalization.CultureInfo.InvariantCulture)) / 10,
                            Y = EeShape.ConvertToMM(double.Parse(transformInfo[7], System.Globalization.CultureInfo.InvariantCulture)) / 10,
                            Z = EeShape.ConvertToMM(double.Parse(transformInfo[8], System.Globalization.CultureInfo.InvariantCulture)) / 10,
                        };
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[API] Error parsing 3D Model Transform: {ex.Message}");
                    Console.WriteLine($"[API] Error parsing 3D Model Transform: {ex.Message}");
                }
            }

            List<string> keysToRemove = new()
            {
                "Add into BOM",
                "Convert to PCB",
                "Symbol",
                "Designator",
                "Footprint",
                "3D Model",
                "3D Model Title",
                "3D Model Transform",
                "Name"
            };
            
            return new ProductInfo
            {
                Description = description,
                Size = size,
                Rotation = rotation,
                Offset = offset,
                Parameters = attributes?.Where(kvp => !keysToRemove.Contains(kvp.Key) && kvp.Value != "-").ToDictionary(kvp => kvp.Key, kvp => kvp.Value) ?? new Dictionary<string, string>()
            };
        }

        public async Task<List<PartInfo>> SearchProductInfoAsync(string lcscId, CancellationToken cancellationToken = default)
        {
            // The server now reads the part number from the query string and ignores the JSON body
            string url = $"https://pro.easyeda.com/api/v2/eda/product/search?keyword={Uri.EscapeDataString(lcscId.Trim())}";
            Debug.WriteLine($"[API] POST Request: {url}");
            Console.WriteLine($"[API] POST Request: {url}");

            var payload = new
            {
                codes = lcscId
            };
            
            string jsonPayload = JsonConvert.SerializeObject(payload);
            Debug.WriteLine($"[API] Request Payload: {jsonPayload}");
            Console.WriteLine($"[API] Request Payload: {jsonPayload}");

            using var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
            
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = content;
            request.Headers.Add("Referer", "https://pro.easyeda.com/editor");
            request.Headers.Add("Origin", "https://pro.easyeda.com");
            request.Headers.Add("X-Requested-With", "XMLHttpRequest");
            request.Headers.Add("sec-fetch-dest", "empty");
            request.Headers.Add("sec-fetch-mode", "cors");
            request.Headers.Add("sec-fetch-site", "same-origin");
            
            try
            {
                using var response = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                Debug.WriteLine($"[API] Response Status: {(int)response.StatusCode} {response.StatusCode}");
                Console.WriteLine($"[API] Response Status: {(int)response.StatusCode} {response.StatusCode}");
                
                response.EnsureSuccessStatusCode();
                string result = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                
                Debug.WriteLine($"[API] Response Length: {result.Length} characters");
                Console.WriteLine($"[API] Response Length: {result.Length} characters");
                LogResponse(result);
                
                JObject obj = JObject.Parse(result);
                JArray products = obj.SelectToken("result.productList") as JArray
                    ?? throw new InvalidDataException("EasyEDA returned no product list.");
                List<PartInfo> productList = new();
                foreach(var product in products)
                {
                    var deviceInfo = product["device_info"] as JObject;
                    var attributes = deviceInfo?["attributes"]?.ToObject<Dictionary<string, string>>();
                    var productInfo = ProductFromAttributes(attributes,
                        (string)deviceInfo?["description"] ?? (string)deviceInfo?["Description"] ?? "");
                    bool hasSymbol = HasValue(deviceInfo?["symbol_info"]);
                    bool hasFootprint = HasValue(deviceInfo?["footprint_info"]);
                    bool has3d = HasValue(deviceInfo?["footprint_info"]?["model_3d"]);
                    productList.Add(new PartInfo
                    {
                        Name = (string)product["mpn"] ?? "",
                        Part = (string)product["number"] ?? "",
                        Description = productInfo.Description ?? "",
                        Info = productInfo,
                        HasSymbol = hasSymbol,
                        HasFootprint = hasFootprint,
                        Has3d = has3d,
                    });
                }
                
                Debug.WriteLine($"[API] Found {productList.Count} products");
                Console.WriteLine($"[API] Found {productList.Count} products");
                return productList;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[API] Error: {ex.Message}");
                Console.WriteLine($"[API] Error: {ex.Message}");
                throw;
            }
        }
        private static bool HasValue(JToken token) => token != null &&
            token.Type != JTokenType.Null &&
            (token.Type != JTokenType.String || !string.IsNullOrWhiteSpace((string)token));
    }
}
