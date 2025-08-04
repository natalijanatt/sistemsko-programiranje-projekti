#nullable enable
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using System.Diagnostics;

namespace drugi_projekat
{
    class Program
    {
        private static readonly string RootDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Root");
        private static readonly ConcurrentDictionary<string, byte[]> ResponseCache = new ConcurrentDictionary<string, byte[]>();

        static async Task Main(string[] args)
        {
            if (!Directory.Exists(RootDirectory))
                Directory.CreateDirectory(RootDirectory);

            int port = 5050;
            var server = new TcpListener(IPAddress.Any, port);
            server.Start();
            Console.WriteLine($"Server pokrenut na http://localhost:{port}");

            while (true)
            {
                var client = await server.AcceptTcpClientAsync();
                //obrada bez blokiranja glavne petlje
                _ = Task.Run(() => HandleClientAsync(client));
            }
        }

        private static async Task HandleClientAsync(TcpClient client)
        {
            var stopwatch = Stopwatch.StartNew();
            int statusCode = 500;
            string? requestLine = null;

            try
            {
                using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.UTF8);
                using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };

                // Čitanje prvog reda zahteva
                requestLine = await reader.ReadLineAsync();
                if (string.IsNullOrEmpty(requestLine))
                    return;

                Console.WriteLine($"[{DateTime.Now}] Zahtev: {requestLine} od {client.Client.RemoteEndPoint}");

                // Preskoči HTTP zaglavlja
                string? header;
                while (!string.IsNullOrEmpty(header = await reader.ReadLineAsync())) { }

                var tokens = requestLine.Split(' ');
                if (tokens.Length < 2 || tokens[0] != "GET")
                {
                    await writer.WriteAsync("HTTP/1.1 405 Method Not Allowed\r\n\r\n");
                    statusCode = 405;
                    return;
                }

                string rawPath = tokens[1];
                string searchKey = WebUtility.UrlDecode(rawPath.TrimStart('/'));

                //Provera keša
                if (ResponseCache.TryGetValue(rawPath, out var cached))
                {
                    Console.WriteLine($"[{DateTime.Now}] Pronađeno u kešu za putanju: {rawPath}");
                    await stream.WriteAsync(cached, 0, cached.Length);
                    statusCode = 200;
                    return;
                }

                string filePath = Path.Combine(RootDirectory, searchKey);
                if (File.Exists(filePath))
                {
                    //Asinhrono čitanje fajla
                    var fileBytes = await File.ReadAllBytesAsync(filePath);
                    var fileName = Path.GetFileName(filePath);
                    var contentType = GetContentType(fileName);

                    var headerString =
                        $"HTTP/1.1 200 OK\r\n" +
                        $"Content-Type: {contentType}\r\n" +
                        $"Content-Length: {fileBytes.Length}\r\n" +
                        $"Content-Disposition: attachment; filename=\"{fileName}\"\r\n\r\n";

                    var headerBytes = Encoding.UTF8.GetBytes(headerString);
                    var responseBytes = Combine(headerBytes, fileBytes);
                    //keširanje
                    ResponseCache[rawPath] = responseBytes;

                    await writer.WriteAsync(headerString);
                    await writer.FlushAsync();
                    await stream.WriteAsync(fileBytes, 0, fileBytes.Length);
                    statusCode = 200;
                }
                else
                {
                    //LINQ za pretragu fajlova
                    var allFiles = Directory.GetFiles(RootDirectory);
                    var matches = allFiles
                        .Select(Path.GetFileName)
                        .Where(name => !string.IsNullOrEmpty(name) &&
                                       name!.IndexOf(searchKey, StringComparison.OrdinalIgnoreCase) >= 0)
                        .ToList();

                    var sb = new StringBuilder();
                    sb.Append("<html><head><meta charset=\"utf-8\"><title>Rezultati pretrage</title></head><body>");
                    if (matches.Count > 0)
                    {
                        foreach (var name in matches)
                            sb.Append($"<a href=\"/{name}\">{name}</a><br/>\n");
                    }
                    else
                    {
                        sb.Append("<p>Nema fajlova koji se poklapaju sa pojmom za pretragu.</p>");
                    }
                    sb.Append("</body></html>");

                    var contentBytes = Encoding.UTF8.GetBytes(sb.ToString());
                    var headerHtml =
                        $"HTTP/1.1 200 OK\r\n" +
                        "Content-Type: text/html; charset=utf-8\r\n" +
                        $"Content-Length: {contentBytes.Length}\r\n\r\n";

                    var headerBytes = Encoding.UTF8.GetBytes(headerHtml);
                    var responseBytes = Combine(headerBytes, contentBytes);
                    //Keširanje
                    ResponseCache[rawPath] = responseBytes;

                    await writer.WriteAsync(headerHtml);
                    await writer.FlushAsync();
                    await stream.WriteAsync(contentBytes, 0, contentBytes.Length);
                    statusCode = 200;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Greška prilikom obrade zahteva: {ex.Message}");
            }
            finally
            {
                stopwatch.Stop();
                Console.WriteLine($"[{DateTime.Now}] {requestLine} -> {statusCode} za {stopwatch.ElapsedMilliseconds} ms");
                client.Close();
            }
        }

        private static string GetContentType(string fileName)
        {
            var ext = Path.GetExtension(fileName).ToLowerInvariant();
            return ext switch
            {
                ".html" or ".htm" => "text/html",
                ".txt" => "text/plain",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".pdf" => "application/pdf",
                _ => "application/octet-stream",
            };
        }

        private static byte[] Combine(byte[] a, byte[] b)
        {
            var c = new byte[a.Length + b.Length];
            Buffer.BlockCopy(a, 0, c, 0, a.Length);
            Buffer.BlockCopy(b, 0, c, a.Length, b.Length);
            return c;
        }
    }
}
