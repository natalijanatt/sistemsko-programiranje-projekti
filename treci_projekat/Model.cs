using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace projekat_tri
{
    public class GitHubComment
    {
        [JsonProperty("id")]
        public long Id { get; set; }

        [JsonProperty("body")]
        public string Body { get; set; }
    }

    public class CommentData
    {
        public string Text { get; set; } = string.Empty;
    }

    public static class StringExt
    {
        public static string IfNullOrWhiteSpace(this string? s, string fallback)
            => string.IsNullOrWhiteSpace(s) ? fallback : s;
    }
}
