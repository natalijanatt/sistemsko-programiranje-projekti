#nullable enable
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Collections.Generic;
using System.Threading;
using System.Diagnostics;

namespace prvi_projekat
{
    class Program
    {
        private static readonly string RootDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Root");
        private static readonly Dictionary<string, byte[]> ResponseCache = new Dictionary<string, byte[]>();
        private static readonly object CacheLock = new object();

        static void Main(string[] args)
        {
            if (!Directory.Exists(RootDirectory))
                Directory.CreateDirectory(RootDirectory);

            int port = 5050;
            var server = new TcpListener(IPAddress.Any, port);
            server.Start();
            Console.WriteLine($"Server pokrenut na http://localhost:{port}");

            while (true)
            {
                var client = server.AcceptTcpClient();
                ThreadPool.QueueUserWorkItem(HandleClient, client);
            }
        }

        private static void HandleClient(object? state)
        {
            var client = (TcpClient)state!;
            var stopwatch = Stopwatch.StartNew();
            int statusCode = 500;
            string? requestLine = null;

            try
            {
                using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.UTF8);
                using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };

                //Čitanje zahteva
                requestLine = reader.ReadLine();
                if (string.IsNullOrEmpty(requestLine))
                    return;

                Console.WriteLine($"[{DateTime.Now}] Zahtev: {requestLine} od {client.Client.RemoteEndPoint}");

                string? header;
                while (!string.IsNullOrEmpty(header = reader.ReadLine())) { }

                //Proveravanje metode
                var tokens = requestLine.Split(' ');
                if (tokens.Length < 2 || tokens[0] != "GET")
                {
                    writer.Write("HTTP/1.1 405 Method Not Allowed\r\n\r\n");
                    statusCode = 405;
                    return;
                }

                string rawPath = tokens[1];
                string searchKey = WebUtility.UrlDecode(rawPath.TrimStart('/'));

                //Provera keša
                lock (CacheLock)
                {
                    if (ResponseCache.TryGetValue(rawPath, out var cached))
                    {
                        Console.WriteLine($"[{DateTime.Now}] Pronađeno u kešu za putanju: {rawPath}");
                        stream.Write(cached, 0, cached.Length);
                        statusCode = 200;
                        return;
                    }
                }

                //Provera da li fajl postoji
                string filePath = Path.Combine(RootDirectory, searchKey);
                if (File.Exists(filePath))
                {
                    var fileBytes = File.ReadAllBytes(filePath);
                    var fileName = Path.GetFileName(filePath);
                    var contentType = GetContentType(fileName);

                    var headerString =
                        $"HTTP/1.1 200 OK\r\n" +
                        $"Content-Type: {contentType}\r\n" +
                        $"Content-Length: {fileBytes.Length}\r\n" +
                        $"Content-Disposition: attachment; filename=\"{fileName}\"\r\n\r\n";

                    //Keširanje
                    lock (CacheLock)
                    {
                        ResponseCache[rawPath] = Combine(Encoding.UTF8.GetBytes(headerString), fileBytes);
                    }

                    //Slanje odgovora
                    writer.Write(headerString);
                    writer.Flush();
                    stream.Write(fileBytes, 0, fileBytes.Length);
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

                    //Formiranje HTML odgovora
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

                    //Keširanje
                    lock (CacheLock)
                    {
                        ResponseCache[rawPath] = Combine(Encoding.UTF8.GetBytes(headerHtml), contentBytes);
                    }

                    //Slanje odgovora
                    writer.Write(headerHtml);
                    writer.Flush();
                    stream.Write(contentBytes, 0, contentBytes.Length);
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
