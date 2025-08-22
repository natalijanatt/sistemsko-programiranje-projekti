using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System;
using System.Net.Http;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using System.Reactive.Linq;
using System.Reactive.Concurrency;


namespace projekat_tri
{
    public class WebServer
    {
        private readonly Subject<HttpContext> _requests = new Subject<HttpContext>();
        public IObservable<HttpContext> Requests => _requests;

        public void Start(string url = "http://localhost:5000")
        {
            var builder = WebApplication.CreateBuilder();
            var app = builder.Build();

            // /analyze?owner=dotnet&repo=runtime&issue=1&topics=5
            app.Map("/analyze", async context =>
            {
                _requests.OnNext(context);

                string owner = context.Request.Query["owner"].ToString().IfNullOrWhiteSpace("dotnet");
                string repo = context.Request.Query["repo"].ToString().IfNullOrWhiteSpace("runtime");
                int issue = int.TryParse(context.Request.Query["issue"], out var i) ? i : 1;
                int topics = int.TryParse(context.Request.Query["topics"], out var t) ? t : 5;

                string? auth = context.Request.Headers.Authorization;
                string? token = null;
                if (!string.IsNullOrWhiteSpace(auth) && auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                    token = auth.Substring("Bearer ".Length).Trim();

                var svc = new GitHubService();
                var cts = new CancellationTokenSource();
                var comments = new System.Collections.Concurrent.ConcurrentBag<CommentData>();
                var done = new TaskCompletionSource();


                RxPipelines.IssueCommentsObservable(svc, owner, repo, issue, token, cts.Token)
                    .Timeout(TimeSpan.FromSeconds(20))
                    .Retry(2)
                    .Do(c => Console.WriteLine($"[RX] Comment {Math.Min(c.Text.Length, 80)} chars received"))
                    .ObserveOn(System.Reactive.Concurrency.NewThreadScheduler.Default)
                    .Subscribe(
                        c => comments.Add(c),
                        ex =>
                        {
                            Console.WriteLine($"[ERROR] {ex.GetType().Name}: {ex.Message}");
                            context.Response.StatusCode = 500;
                            done.TrySetResult();
                        },
                        () => done.TrySetResult()
                    );

                await done.Task;

                if (comments.IsEmpty)
                {
                    await context.Response.WriteAsync("No comments found.");
                    return;
                }

                var modeler = new TopicModeler(seed: 1);
                var report = modeler.AnalyzeTopics(comments, numberOfTopics: topics);

                context.Response.ContentType = "text/plain; charset=utf-8";
                await context.Response.WriteAsync(report);
            });

            app.RunAsync(url);
            Console.WriteLine($"Web server running at {url}");
        }
    }
}
