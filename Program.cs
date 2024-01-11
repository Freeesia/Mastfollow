using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Mastonet;
using Mastonet.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

var app = ConsoleApp.CreateBuilder(args)
    .ConfigureServices((c, s) => s.Configure<ConsoleOptions>(c.Configuration).AddHttpClient())
    .ConfigureLogging((c, l) => l.AddConfiguration(c.Configuration).AddSentry())
    .Build();
app.AddRootCommand(Run);

using (app.Logger.BeginScope("startup"))
{
    app.Logger.LogInformation($"App: {app.Environment.ApplicationName}");
    app.Logger.LogInformation($"Env: {app.Environment.EnvironmentName}");
    var assembly = Assembly.GetExecutingAssembly();
    var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? assembly.GetName().Version?.ToString();
    app.Logger.LogInformation($"Ver: {version}");
}

await app.RunAsync();

static async Task Run(ILogger<Program> logger, IOptions<ConsoleOptions> options, IHttpClientFactory factory)
{
    var (mastodonUrl, mastodonToken, followerThreshold) = options.Value;
    var client = new MastodonClient(mastodonUrl, mastodonToken, factory.CreateClient());

    var me = await client.GetCurrentUser();
    var follows = (await client.GetAccountFollowing(me.Id)).Select(x => x.Id).ToHashSet();

    var pub = client.GetPublicStreaming();
    pub.OnUpdate += async (_, e) =>
    {
        logger.LogInformation($"Update: {e.Status.Account.AccountName} {e.Status.Id}");
        await FollowIfTarget(logger, client, e.Status, follows, followerThreshold);
    };
    var home = client.GetUserStreaming();
    home.OnUpdate += async (_, e) =>
    {
        logger.LogInformation($"Home: {e.Status.Account.AccountName} {e.Status.Id}");
        var status = e.Status.Reblog;
        if (status is null)
        {
            return;
        }
        await FollowIfTarget(logger, client, status, follows, followerThreshold);
    };
    await Task.WhenAll(pub.Start(), home.Start());
}

static async Task FollowIfTarget(ILogger logger, MastodonClient client, Status status, ISet<string> follows, int followerThreshold)
{
    // フォロワー数が閾値未満ならフォローしない
    var follow = status.Account.FollowersCount > followerThreshold;
    // 既にフォローしているならフォローしない
    follow &= !follows.Contains(status.Account.Id);
    // botはフォローしない
    follow &= !status.Account.Bot ?? false;
    // 日本語以外の投稿はフォローしない
    follow &= status.Language == "ja";
    // フォロワーよりフォローしているならフォローしない
    follow &= status.Account.FollowingCount <= status.Account.FollowersCount;
    if (!follow)
    {
        return;
    }
    await client.Follow(status.Account.Id, true);
    follows.Add(status.Account.Id);
    logger.LogInformation($"Followed: {status.Account.AccountName}");
}

class ConsoleOptions
{
    public required string MastodonUrl { get; init; }
    public required string MastodonToken { get; init; }
    public int FollowerThreshold { get; init; } = 1000;

    public void Deconstruct(out string mastodonUrl, out string mastodonToken, out int followerThreshold)
    {
        mastodonUrl = MastodonUrl;
        mastodonToken = MastodonToken;
        followerThreshold = FollowerThreshold;
    }
}
