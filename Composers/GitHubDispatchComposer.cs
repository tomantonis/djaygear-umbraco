using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace YourProject.Composers
{
    public class GitHubDispatchComposer : IComposer
    {
        public void Compose(IUmbracoBuilder builder)
        {
            // Trigger on publish
            builder.AddNotificationHandler<ContentPublishedNotification, GitHubDispatchHandler>();

            // Trigger on unpublish
            builder.AddNotificationHandler<ContentUnpublishedNotification, GitHubDispatchHandler>();
        }
    }

    public class GitHubDispatchHandler : INotificationHandler<ContentPublishedNotification>,
                                        INotificationHandler<ContentUnpublishedNotification>
    {
        private readonly IConfiguration _config;
        private readonly ILogger<GitHubDispatchHandler> _logger;
        private static readonly HttpClient _httpClient = new HttpClient();

        public GitHubDispatchHandler(IConfiguration config, ILogger<GitHubDispatchHandler> logger)
        {
            _config = config;
            _logger = logger;
        }

        public void Handle(ContentPublishedNotification notification)
        {
            SendToGitHub(notification.PublishedEntities, "content_published");
        }

        public void Handle(ContentUnpublishedNotification notification)
        {
            SendToGitHub(notification.UnpublishedEntities, "content_unpublished");
        }

        private void SendToGitHub(IEnumerable<Umbraco.Cms.Core.Models.IContent> contents, string eventType)
        {
            var owner = _config["GitHub:Owner"];
            var repo = _config["GitHub:Repo"];
            var token = _config["GitHub:Token"];

            if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo) || string.IsNullOrWhiteSpace(token))
            {
                _logger.LogWarning("GitHubDispatchHandler: Missing configuration for Owner/Repo/Token.");
                return;
            }

            var url = $"https://api.github.com/repos/{owner}/{repo}/dispatches";

            foreach (var content in contents)
            {
                var payload = new
                {
                    event_type = eventType,
                    client_payload = new
                    {
                        id = content.Id,
                        name = content.Name,
                        contentType = content.ContentType.Alias,
                        updateDate = content.UpdateDate
                    }
                };

                var json = JsonSerializer.Serialize(payload);
                var contentBody = new StringContent(json, Encoding.UTF8, "application/json");

                try
                {
                    _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Umbraco-Cms/17.0.1");
                    _httpClient.DefaultRequestHeaders.Accept.Clear();
                    _httpClient.DefaultRequestHeaders.Accept.Add(
                        new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));
                    _httpClient.DefaultRequestHeaders.Authorization =
                        new AuthenticationHeaderValue("token", token);

                    // Fire-and-forget async
                    _ = _httpClient.PostAsync(url, contentBody).ContinueWith(task =>
                    {
                        if (task.IsCompletedSuccessfully)
                        {
                            _logger.LogInformation("GitHubDispatchHandler: Successfully dispatched '{EventType}' for content {ContentName} ({ContentId})",
                                eventType, content.Name, content.Id);
                        }
                        else
                        {
                            _logger.LogError(task.Exception, "GitHubDispatchHandler: Failed to dispatch '{EventType}' for content {ContentName} ({ContentId})",
                                eventType, content.Name, content.Id);
                        }
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "GitHubDispatchHandler: Exception while dispatching '{EventType}' for content {ContentName} ({ContentId})",
                        eventType, content.Name, content.Id);
                }
            }
        }
    }
}
