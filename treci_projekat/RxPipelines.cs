using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;

namespace projekat_tri
{
    public static class RxPipelines
    {
        public static IObservable<CommentData> IssueCommentsObservable(
            GitHubService svc, string owner, string repo, int issue, string? token = null, CancellationToken ct = default)
        {
            return Observable
                .FromAsync(async () =>
                {
                    var acc = new List<CommentData>();
                    await foreach (var c in svc.FetchIssueCommentsPagedAsync(owner, repo, issue, token, 100, ct))
                    {
                        if (!string.IsNullOrWhiteSpace(c.Body))
                            acc.Add(new CommentData { Text = c.Body! });
                    }
                    return acc;
                })
                .SelectMany(list => list)
                .SubscribeOn(System.Reactive.Concurrency.TaskPoolScheduler.Default);
        }
    }
}
