using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Linq;

namespace ClearDesk
{
    public static class UpdateTests
    {
        sealed class FakeHandler : HttpMessageHandler
        {
            public HttpStatusCode Status = HttpStatusCode.OK;
            public string Json = "[]";
            public string Method, Url, Agent;
            public bool HasBody, HasAuthorization;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
            {
                cancellation.ThrowIfCancellationRequested();
                Method = request.Method.Method; Url = request.RequestUri.AbsoluteUri; Agent = request.Headers.UserAgent.ToString();
                HasBody = request.Content != null; HasAuthorization = request.Headers.Authorization != null;
                return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent(Json) });
            }
        }
        public static void Run(Action<bool, string> check, string root, ManagerWindow manager)
        {
            var state = Settings.Default(); DateTime now = new DateTime(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);
            check(!state.AutoUpdateReminder && !UpdateReminderPolicy.IsDue(state, now), "automatic update requests are opt-in and disabled by default");
            state.AutoUpdateReminder = true;
            check(UpdateReminderPolicy.IsDue(state, now), "first opt-in schedules an update query");
            state.LastUpdateCheckUtc = now;
            check(!UpdateReminderPolicy.IsDue(state, now.AddHours(23)) && UpdateReminderPolicy.IsDue(state, now.AddDays(1)), "automatic queries are limited to once per day");
            check(UpdateReminderPolicy.IsDue(state, now.AddHours(-1)), "clock rollback does not permanently suppress update checks");
            string releases = "[{\"tag_name\":\"v0.5.9\",\"draft\":false},{\"tag_name\":\"v0.5.10\",\"draft\":false,\"prerelease\":true},{\"tag_name\":\"v8.0.0\",\"draft\":true},{\"tag_name\":\"nightly\"}]";
            AvailableUpdate update = GithubUpdateChecker.SelectRelease(releases, "0.5.1");
            check(update.Version == "0.5.10", "numeric version sorting includes public test releases and excludes drafts and nightly tags");
            check(update.Url == "https://github.com/YDZ-syj/ClearDesk/releases/tag/v0.5.10", "download links always use the project's GitHub release page");
            check(GithubUpdateChecker.SelectRelease(releases, "0.5.10") == null && GithubUpdateChecker.SelectRelease(releases, "0.6.0") == null, "current and newer local versions are never offered a downgrade");
            check(GithubUpdateChecker.SelectRelease("[]", "0.5.1") == null, "repositories without public releases produce no update prompt");
            check(GithubUpdateChecker.SelectRelease("[{\"tag_name\":\"v999999999.1.0\"},{\"tag_name\":\"v0.6.0/../../evil\"}]", "0.5.1") == null, "invalid version numbers and path-like tags are ignored");
            bool rejected = false;
            try { GithubUpdateChecker.SelectRelease("not json", "0.5.1"); } catch { rejected = true; }
            check(rejected, "invalid network responses cannot be reported as the latest version");
            check(UpdateReminderPolicy.ShouldNotify(state, update), "a new public version triggers an opted-in reminder");
            state.LastNotifiedUpdate = update.Version;
            check(!UpdateReminderPolicy.ShouldNotify(state, update), "the same new version is not announced repeatedly");
            state.AutoUpdateReminder = false;
            check(!UpdateReminderPolicy.ShouldNotify(state, new AvailableUpdate { Version = "0.6.0" }), "disabling reminders suppresses pending automatic notifications");
            state.AutoUpdateReminder = true;
            var store = new SettingsStore(Path.Combine(root, "update-state", "settings.json")); store.Save(state);
            var loaded = store.Load();
            check(loaded.AutoUpdateReminder && loaded.LastUpdateCheckUtc == now && loaded.LastNotifiedUpdate == "0.5.10", "reminder choice and throttle survive application restart");
            File.WriteAllText(store.FilePath, "{\"Version\":1,\"Zones\":[]}");
            check(!store.Load().AutoUpdateReminder, "old configurations migrate with network reminders disabled");
            check(manager.CreateMoreMenu().Items.OfType<MenuItem>().Any(m => (string)m.Header == "检查更新") && manager.CreateMoreMenu().Items.OfType<MenuItem>().Any(m => (string)m.Header == "自动提醒新版本" && m.IsCheckable && !m.IsChecked), "update action and optional reminders are visible in the menu");

            var handler = new FakeHandler { Json = releases };
            var checker = new GithubUpdateChecker(delegate { return handler; });
            check(checker.CheckAsync("0.5.1", CancellationToken.None).GetAwaiter().GetResult().Version == "0.5.10", "HTTP update transport selects the latest public version");
            check(handler.Method == "GET" && handler.Url == GithubUpdateChecker.ReleasesUrl && !handler.HasBody && !handler.HasAuthorization && handler.Agent == "ClearDesk/" + AppBrand.Version, "update requests contain no credentials, file paths, or machine-specific identifiers");
            handler = new FakeHandler { Status = HttpStatusCode.Forbidden };
            checker = new GithubUpdateChecker(delegate { return handler; });
            rejected = false; try { checker.CheckAsync("0.5.1", CancellationToken.None).GetAwaiter().GetResult(); } catch (HttpRequestException) { rejected = true; }
            check(rejected, "GitHub rate limits and HTTP failures are not mistaken for no updates");
            handler = new FakeHandler { Json = releases }; checker = new GithubUpdateChecker(delegate { return handler; });
            using (var canceled = new CancellationTokenSource())
            {
                canceled.Cancel(); rejected = false;
                try { checker.CheckAsync("0.5.1", canceled.Token).GetAwaiter().GetResult(); } catch (OperationCanceledException) { rejected = true; }
                check(rejected, "closing the application cancels pending update queries");
            }
        }
    }
}
