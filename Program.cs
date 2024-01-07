using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Mastonet;
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

    var hoge = await client.GetPublicTimeline();

    var stream = client.GetPublicStreaming();
    stream.OnUpdate += async (_, e) =>
    {
        if (e.Status.Account.FollowersCount < followerThreshold)
        {
            return;
        }
        // TODO: リレー関係にあるドメインは除外する
        if (!e.Status.Account.AccountName.EndsWith("@misskey.io"))
        {
            return;
        }
        await client.Follow(e.Status.Account.AccountName);
    };
    stream.OnDelete += (_, e) => logger.LogInformation($"Deleted: {e.StatusId}");
    stream.OnNotification += (_, e) => logger.LogInformation($"Notification: {e.Notification.Type}");
    stream.OnConversation += (_, e) => logger.LogInformation($"Conversation: {e.Conversation.Id}");
    stream.OnFiltersChanged += (_, e) => logger.LogInformation("Filters changed");

    await stream.Start();
    logger.LogInformation("Stream started");
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
