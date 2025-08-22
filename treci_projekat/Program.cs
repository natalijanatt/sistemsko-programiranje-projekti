using projekat_tri;
using System;
using System.Reactive.Linq;

class Program
{
    static void Main()
    {
        var server = new WebServer();

        server.Requests
            .Subscribe(ctx =>
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var method = ctx.Request.Method;
                var path = $"{ctx.Request.Path}{ctx.Request.QueryString}";
                Console.WriteLine("-------------------------------------------------");
                Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {method} {path}");

                ctx.Response.OnCompleted(() =>
                {
                    sw.Stop();
                    Console.WriteLine($"Status: {ctx.Response.StatusCode} | {sw.ElapsedMilliseconds} ms");
                    Console.WriteLine("-------------------------------------------------");
                    return System.Threading.Tasks.Task.CompletedTask;
                });
            });

        server.Start("http://localhost:5000");

        Console.ReadLine();
    }
}
