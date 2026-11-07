using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace DashboardStarter.Api.Email;

/// <summary>
/// Sends email through Resend's REST API (https://resend.com/docs/api-reference/emails/send-email):
/// one POST with a JSON body. A plain <see cref="HttpClient"/> from IHttpClientFactory, set up
/// in <see cref="EmailSetup"/> with the API key and the User-Agent Resend requires. No SDK:
/// it would wrap this one call.
/// </summary>
/// <remarks>
/// The key is a sending-only key restricted to one domain, so a leaked key can't read the
/// account or send as anyone else. It's never logged, and neither are the address or the
/// link: logs say which user, and Resend's id for the message, which is enough to find it in
/// Resend's dashboard. No retries: a failed reset email is logged, and the person can ask again.
/// </remarks>
public sealed class ResendEmailSender : IEmailSender
{
  private readonly IHttpClientFactory _httpClientFactory;
  private readonly EmailStatus _status;
  private readonly ILogger<ResendEmailSender> _logger;

  public ResendEmailSender(IHttpClientFactory httpClientFactory, EmailStatus status, ILogger<ResendEmailSender> logger)
  {
    _httpClientFactory = httpClientFactory;
    _status = status;
    _logger = logger;
  }

  public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
  {
    HttpClient client = _httpClientFactory.CreateClient(EmailSetup.ResendClientName);
    ResendEmail body = new(_status.From!, [message.To], message.Subject, message.Text, message.Html);

    using HttpResponseMessage response = await client.PostAsJsonAsync("emails", body, cancellationToken);
    if (response.IsSuccessStatusCode)
    {
      ResendResult? result = await response.Content.ReadFromJsonAsync<ResendResult>(cancellationToken);
      _logger.LogInformation("Sent an email to user {UserId} through Resend: id {ResendId}.", message.UserId, result?.Id);
      return;
    }

    // Resend's error body is {"statusCode":..., "name":"validation_error", "message":"..."}.
    // Log the name only: the message can quote the addresses it was given.
    string? errorName = await ReadErrorNameAsync(response, cancellationToken);
    _logger.LogError(
      "Resend refused an email to user {UserId}: HTTP {Status}, {ErrorName}.",
      message.UserId,
      (int)response.StatusCode,
      errorName ?? "no error name");
  }

  private static async Task<string?> ReadErrorNameAsync(HttpResponseMessage response, CancellationToken cancellationToken)
  {
    try
    {
      ResendError? error = await response.Content.ReadFromJsonAsync<ResendError>(cancellationToken);
      return error?.Name;
    }
    catch (JsonException)
    {
      return null;
    }
  }

  private sealed record ResendEmail(
    [property: JsonPropertyName("from")] string From,
    [property: JsonPropertyName("to")] string[] To,
    [property: JsonPropertyName("subject")] string Subject,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("html")] string Html);

  private sealed record ResendResult([property: JsonPropertyName("id")] string? Id);

  private sealed record ResendError([property: JsonPropertyName("name")] string? Name);
}
