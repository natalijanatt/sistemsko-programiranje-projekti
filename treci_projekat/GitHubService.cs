using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace projekat_tri
{
    public class GitHubService
    {
        private static readonly HttpClient client = new HttpClient();
        public async IAsyncEnumerable<GitHubComment> FetchIssueCommentsPagedAsync(
            string owner, string repo, int issueNumber, string? token = null, int perPage = 100,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            string? url = $"https://api.github.com/repos/{owner}/{repo}/issues/{issueNumber}/comments?per_page={perPage}";
            while (url != null)
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Add("User-Agent", "GitHubCommentFetcher");
                req.Headers.Add("Accept", "application/vnd.github+json");
                req.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
                if (!string.IsNullOrEmpty(token))
                    req.Headers.Add("Authorization", $"Bearer {token}");

                using var res = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
                res.EnsureSuccessStatusCode();

                var json = await res.Content.ReadAsStringAsync(ct);
                var page = JsonConvert.DeserializeObject<List<GitHubComment>>(json) ?? new();

                foreach (var c in page)
                {
                    if (ct.IsCancellationRequested) yield break;
                    yield return c;
                }

                //Paginacija preko Link headera
                if (res.Headers.TryGetValues("Link", out var links))
                {
                    url = ParseNextLink(links.FirstOrDefault());
                }
                else url = null;
            }
        }

        private static string? ParseNextLink(string? linkHeader)
        {
            if (string.IsNullOrEmpty(linkHeader)) return null;
            var parts = linkHeader.Split(',');
            foreach (var p in parts)
            {
                if (p.Contains("rel=\"next\""))
                {
                    var start = p.IndexOf('<') + 1;
                    var end = p.IndexOf('>');
                    if (start > 0 && end > start) return p.Substring(start, end - start);
                }
            }
            return null;
        }
    }
}
