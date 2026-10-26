using System.Net;
using Nacho.Core;
using Xunit;

namespace Nacho.Tests;

public sealed class UpdateTests
{
    private sealed class Fake : HttpMessageHandler
    {
        private readonly HttpStatusCode _code;
        private readonly string _body;
        public HttpRequestMessage? Seen { get; private set; }

        public Fake(HttpStatusCode code, string body)
        {
            _code = code;
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Seen = request;
            return Task.FromResult(new HttpResponseMessage(_code) { Content = new StringContent(_body) });
        }
    }

    private static string Release(string tag, bool pre = false, string url = "https://github.com/csabakovacs314/NACHO/releases/tag/") =>
        $$"""{"tag_name":"{{tag}}","name":"NACHO {{tag}}","draft":false,"prerelease":{{(pre ? "true" : "false")}},"html_url":"{{url}}{{tag}}"}""";

    [Theory]
    [InlineData("v0.8.0", "0.8.0")]
    [InlineData("0.10.2", "0.10.2")]
    [InlineData("v1.0", "1.0.0")]
    [InlineData("v0.8.0-beta", null)]
    [InlineData("latest", null)]
    [InlineData("", null)]
    public void Tags_read_as_versions(string tag, string? expected)
    {
        Assert.Equal(expected is null ? null : Version.Parse(expected), UpdateCheck.ParseTag(tag));
    }

    [Fact]
    public async Task A_newer_release_is_found_and_the_request_is_plain()
    {
        var fake = new Fake(HttpStatusCode.OK, Release("v0.10.0"));
        var r = await UpdateCheck.CheckAsync(new Version(0, 9, 0), fake);
        Assert.Equal(UpdateCheck.Outcome.Newer, r.Outcome);
        Assert.Equal(new Version(0, 10, 0), r.Update!.Version);
        Assert.Equal("https://github.com/csabakovacs314/NACHO/releases/tag/v0.10.0", r.Update.PageUrl);
        Assert.Equal(UpdateCheck.LatestReleaseApi, fake.Seen!.RequestUri!.ToString());
        Assert.Equal("NACHO/0.9.0", fake.Seen.Headers.UserAgent.ToString());
        Assert.Null(fake.Seen.Headers.Authorization);
    }

    [Fact]
    public async Task Same_or_older_release_means_up_to_date()
    {
        Assert.Equal(UpdateCheck.Outcome.UpToDate, (await UpdateCheck.CheckAsync(new Version(0, 7, 0), new Fake(HttpStatusCode.OK, Release("v0.7.0")))).Outcome);
        Assert.Equal(UpdateCheck.Outcome.UpToDate, (await UpdateCheck.CheckAsync(new Version(0, 8, 0), new Fake(HttpStatusCode.OK, Release("v0.7.1")))).Outcome);
    }

    [Fact]
    public async Task Failures_never_claim_an_update()
    {
        Assert.Equal(UpdateCheck.Outcome.Failed, (await UpdateCheck.CheckAsync(new Version(0, 7, 0), new Fake(HttpStatusCode.Forbidden, "rate limited"))).Outcome);
        Assert.Equal(UpdateCheck.Outcome.Failed, (await UpdateCheck.CheckAsync(new Version(0, 7, 0), new Fake(HttpStatusCode.OK, "not json"))).Outcome);
        Assert.Equal(UpdateCheck.Outcome.Failed, (await UpdateCheck.CheckAsync(new Version(0, 7, 0), new Fake(HttpStatusCode.OK, Release("v9.0.0", pre: true)))).Outcome);
    }

    [Fact]
    public void Only_nachos_own_release_page_is_opened()
    {
        var info = UpdateCheck.ParseRelease(Release("v0.8.0", url: "https://evil.example/x/"));
        Assert.Equal(UpdateCheck.ReleasesPage, info!.PageUrl);
        Assert.False(UpdateCheck.IsReleasePage("http://github.com/csabakovacs314/NACHO/releases/tag/v1"));
        Assert.True(UpdateCheck.IsReleasePage("https://github.com/csabakovacs314/NACHO/releases/tag/v0.8.0"));
    }

    [Fact]
    public void The_automatic_check_runs_at_most_about_once_a_day()
    {
        var now = new DateTime(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);
        Assert.True(UpdateCheck.Due(null, now));
        Assert.False(UpdateCheck.Due(now.AddHours(-3), now));
        Assert.True(UpdateCheck.Due(now.AddHours(-21), now));
        Assert.True(UpdateCheck.Due(now.AddDays(3), now)); // a clock that went backwards does not block it forever
    }
}
