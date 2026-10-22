using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Dixon.CommandCenter.API.Services;

public sealed class SmsOtpService(HttpClient httpClient, IConfiguration configuration)
{
    public async Task SendAsync(string phoneNumber, string message, CancellationToken cancellationToken)
    {
        var accountSid = configuration["Sms:AccountSid"];
        var authToken = configuration["Sms:AuthToken"];
        var fromNumber = configuration["Sms:FromPhoneNumber"];
        if (string.IsNullOrWhiteSpace(accountSid) || string.IsNullOrWhiteSpace(authToken) || string.IsNullOrWhiteSpace(fromNumber))
            throw new InvalidOperationException("SMS provider is not configured.");

        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://api.twilio.com/2010-04-01/Accounts/{Uri.EscapeDataString(accountSid)}/Messages.json");
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{accountSid}:{authToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["To"] = phoneNumber,
            ["From"] = fromNumber,
            ["Body"] = message
        });
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            string? code = null;
            try
            {
                using var document = JsonDocument.Parse(body);
                if (document.RootElement.TryGetProperty("code", out var codeElement)) code = codeElement.ToString();
            }
            catch (JsonException) { }
            throw new SmsDeliveryException(code);
        }
    }

    public static string GetUserMessage(string? code) => code switch
    {
        "21608" => "Twilio trial account: verify this recipient number in Twilio Console, or upgrade the account before sending to unverified numbers.",
        "21211" => "Twilio says the recipient number is invalid. Check the number and enter it in international format, such as +919876543210.",
        "21606" or "21210" or "21212" => "Twilio rejected the sender number. Check that Sms:FromPhoneNumber is an active SMS-capable Twilio number on this account.",
        "21408" => "SMS to this country is not enabled for the Twilio account. Enable the destination country in Twilio Geo Permissions.",
        "20003" => "Twilio rejected the account credentials. Check Account SID and Auth Token.",
        _ when !string.IsNullOrWhiteSpace(code) => $"Twilio rejected the SMS (error {code}). Check the message details in Twilio Console.",
        _ => "The SMS provider rejected the message. Check the message details in Twilio Console."
    };
}

public sealed class SmsDeliveryException(string? providerCode) : Exception("SMS provider rejected the message.")
{
    public string? ProviderCode { get; } = providerCode;
}
