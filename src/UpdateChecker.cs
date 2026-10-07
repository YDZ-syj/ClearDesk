using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace ClearDesk
{
    public sealed class AvailableUpdate
    {
        public string Version;
        public string Url;
    }
    public interface IUpdateChecker
    {
        Task<AvailableUpdate> CheckAsync(string currentVersion, CancellationToken cancellation);
    }
    [DataContract]
    internal sealed class GithubRelease
    {
        [DataMember(Name = "tag_name")] public string Tag { get; set; }
        [DataMember(Name = "draft")] public bool Draft { get; set; }
    }
    public sealed class GithubUpdateChecker : IUpdateChecker
    {
        internal const string ReleasesUrl = "https://api.github.com/repos/YDZ-syj/ClearDesk/releases?per_page=20";
        readonly Func<HttpMessageHandler> handlerFactory;
        public GithubUpdateChecker() : this(delegate { return new HttpClientHandler { AllowAutoRedirect = false }; }) { }
        internal GithubUpdateChecker(Func<HttpMessageHandler> handlerFactory) { this.handlerFactory = handlerFactory; }
        public async Task<AvailableUpdate> CheckAsync(string currentVersion, CancellationToken cancellation)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            using (var client = new HttpClient(handlerFactory()) { Timeout = TimeSpan.FromSeconds(15) })
            using (var request = new HttpRequestMessage(HttpMethod.Get, ReleasesUrl))
            {
                request.Headers.UserAgent.ParseAdd("ClearDesk/" + AppBrand.Version);
                request.Headers.Accept.ParseAdd("application/vnd.github+json");
                request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
                using (var response = await client.SendAsync(request, cancellation).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (json.Length > 1024 * 1024) throw new InvalidDataException("版本信息过大，请稍后重试。");
                    cancellation.ThrowIfCancellationRequested();
                    return SelectRelease(json, currentVersion);
                }
            }
        }
        internal static AvailableUpdate SelectRelease(string json, string currentVersion)
        {
            Version current = ParseVersion(currentVersion);
            if (current == null) throw new ArgumentException("当前版本号无效。");
            List<GithubRelease> releases;
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                releases = (List<GithubRelease>)new DataContractJsonSerializer(typeof(List<GithubRelease>)).ReadObject(stream);
            if (releases == null) throw new InvalidDataException("无法读取 GitHub 版本信息。");
            // Public test releases are included; this project currently ships test versions.
            var newest = releases.Where(r => r != null && !r.Draft).Select(r => new { Tag = r.Tag, Version = ParseVersion(r.Tag) })
                .Where(r => r.Version != null && r.Version > current).OrderByDescending(r => r.Version).FirstOrDefault();
            return newest == null ? null : new AvailableUpdate { Version = newest.Version.ToString(3), Url = "https://github.com/YDZ-syj/ClearDesk/releases/tag/" + Uri.EscapeDataString(newest.Tag) };
        }
        static Version ParseVersion(string value)
        {
            if (value == null || !Regex.IsMatch(value, "^v?[0-9]{1,7}\\.[0-9]{1,7}\\.[0-9]{1,7}$")) return null;
            Version version; return System.Version.TryParse(value.TrimStart('v'), out version) ? version : null;
        }
    }
    public static class UpdateReminderPolicy
    {
        public static bool IsDue(Settings settings, DateTime nowUtc)
        { return settings.AutoUpdateReminder && (settings.LastUpdateCheckUtc == DateTime.MinValue || nowUtc < settings.LastUpdateCheckUtc || nowUtc - settings.LastUpdateCheckUtc >= TimeSpan.FromDays(1)); }
        public static bool ShouldNotify(Settings settings, AvailableUpdate update)
        { return settings.AutoUpdateReminder && update != null && !string.Equals(settings.LastNotifiedUpdate, update.Version, StringComparison.Ordinal); }
    }
}
