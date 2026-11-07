using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DashboardStarter.Api.Email;

/// <summary>
/// Emails waiting to be sent. A request adds one and answers straight away; the
/// <see cref="EmailQueueWorker"/> sends it moments later. That way "forgot password" answers
/// just as fast whether or not an email goes out, and never waits on the email service.
/// </summary>
/// <remarks>
/// Built from .NET's own pieces (a Channel and a hosted BackgroundService), no job library.
/// The trade-off: the queue lives in memory, so an email queued in the second the app stops
/// is lost, and the person asks again. For a password reset that's acceptable; for email that
/// must never be lost, you'd want a table as the queue instead.
/// </remarks>
public sealed class EmailQueue
{
  // Bounded, so a flood of requests can't grow memory without limit. When it's full, new
  // emails are refused (and logged) rather than old ones dropped.
  private readonly Channel<EmailMessage> _channel = Channel.CreateBounded<EmailMessage>(
    new BoundedChannelOptions(100) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });

  /// <summary>Returns false if the queue is full; the email isn't sent.</summary>
  public bool TryEnqueue(EmailMessage message)
  {
    return _channel.Writer.TryWrite(message);
  }

  public ChannelReader<EmailMessage> Reader => _channel.Reader;
}

/// <summary>Sends the queued emails, one at a time, for as long as the app runs.</summary>
public sealed class EmailQueueWorker : BackgroundService
{
  private readonly EmailQueue _queue;
  private readonly IEmailSender _sender;
  private readonly ILogger<EmailQueueWorker> _logger;

  public EmailQueueWorker(EmailQueue queue, IEmailSender sender, ILogger<EmailQueueWorker> logger)
  {
    _queue = queue;
    _sender = sender;
    _logger = logger;
  }

  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    try
    {
      await foreach (EmailMessage message in _queue.Reader.ReadAllAsync(stoppingToken))
      {
        try
        {
          await _sender.SendAsync(message, stoppingToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
          // One failed email must not stop the ones after it.
          _logger.LogError(exception, "Sending an email to user {UserId} failed.", message.UserId);
        }
      }
    }
    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
    {
      // The app is stopping.
    }
  }
}
