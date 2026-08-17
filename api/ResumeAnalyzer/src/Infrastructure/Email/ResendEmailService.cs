using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ResumeAnalyzer.Application.Common.Interfaces;

namespace ResumeAnalyzer.Infrastructure.Email;

public class ResendEmailService : IEmailService
{
    private readonly HttpClient _http;
    private readonly ResendOptions _options;
    private readonly ILogger<ResendEmailService> _logger;

    public ResendEmailService(
        HttpClient http,
        IOptions<ResendOptions> options,
        ILogger<ResendEmailService> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public Task SendEmailConfirmationAsync(string toEmail, string confirmationLink, CancellationToken cancellationToken = default) =>
        SendAsync(
            toEmail,
            "Confirm your email address",
            $"""
             <p>Welcome to Resume Analyzer.</p>
             <p><a href="{confirmationLink}">Confirm your email address</a></p>
             <p>This link expires in 24 hours. If you didn't create an account, ignore this email.</p>
             """,
            cancellationToken);

    public Task SendPasswordResetAsync(string toEmail, string resetLink, CancellationToken cancellationToken = default) =>
        SendAsync(
            toEmail,
            "Reset your password",
            $"""
             <p>We received a request to reset your password.</p>
             <p><a href="{resetLink}">Choose a new password</a></p>
             <p>This link expires in 24 hours. If you didn't request this, ignore this email.</p>
             """,
            cancellationToken);

    private async Task SendAsync(string to, string subject, string html, CancellationToken cancellationToken)
    {
        var payload = new ResendEmailRequest(
            From: $"{_options.FromName} <{_options.FromEmail}>",
            To: [to],
            Subject: subject,
            Html: html);

        using var request = new HttpRequestMessage(HttpMethod.Post, "emails")
        {
            Content = JsonContent.Create(payload)
        };

        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        using var response = await _http.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            _logger.LogError(
                "Resend rejected the message. Status {Status}. Body {Body}",
                (int)response.StatusCode, body);

            throw new InvalidOperationException($"Resend returned {(int)response.StatusCode}.");
        }

        var sent = await response.Content.ReadFromJsonAsync<ResendEmailResponse>(cancellationToken);

        _logger.LogInformation("Email sent via Resend. MessageId {MessageId}", sent?.Id);
    }

    private sealed record ResendEmailRequest(
        [property: JsonPropertyName("from")] string From,
        [property: JsonPropertyName("to")] string[] To,
        [property: JsonPropertyName("subject")] string Subject,
        [property: JsonPropertyName("html")] string Html);

    private sealed record ResendEmailResponse(
        [property: JsonPropertyName("id")] string Id);
}
