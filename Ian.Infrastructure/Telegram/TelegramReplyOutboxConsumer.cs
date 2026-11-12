using System.Text.Json;
using Ian.Application.Telegram;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ian.Infrastructure.Telegram;

public sealed class TelegramReplyOutboxConsumer(
    ITelegramReplyOutbox store, ITelegramTextSender sender, IOptions<TelegramOptions> telegram,
    TimeProvider clock, ILogger<TelegramReplyOutboxConsumer>? logger = null)
{
    public async Task<bool> ProcessNextAsync(CancellationToken ct, long? updateId = null)
    {
        var claim = await store.ClaimAsync(clock.GetUtcNow(), updateId, ct);
        if (claim is null) return false;
        logger?.LogInformation("Update {UpdateId} stage=send_started attempt={Attempt}.", claim.UpdateId, claim.Number);
        TelegramTextSendResult result;
        if (!Authorized(claim, telegram.Value.AdministratorUserId))
            result = new(TelegramTextSendStatus.UnauthorizedRecipient, 0);
        else
        {
            try { result = await sender.SendAsync(new(claim.UpdateId, claim.PayloadJson), claim.Text, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception) { result = new(TelegramTextSendStatus.UnexpectedFailure, 0, DeliveryUncertain: true); }
        }
        var persisted = await store.FinishAsync(claim, result, clock.GetUtcNow(), ct);
        var decision = TelegramReplyDeliveryPolicy.Classify(result, claim.Number, clock.GetUtcNow());
        logger?.LogInformation("Update {UpdateId} stage=send_finished state={State} outcome={Outcome} attempt={Attempt} persisted={Persisted} retry_scheduled={Retry}.",
            claim.UpdateId, decision.State, result.Status, claim.Number, persisted, decision.Next is not null);
        return true;
    }

    private static bool Authorized(TelegramReplyClaim claim, long admin)
    {
        if (claim.UserId != admin) return false;
        try
        {
            using var json = JsonDocument.Parse(claim.PayloadJson);
            var root = json.RootElement;
            return root.GetProperty("update_id").GetInt64() == claim.UpdateId &&
                TelegramUpdateAuthorization.Evaluate(root, admin) == TelegramAuthorizationDecision.Authorized &&
                root.GetProperty("message").GetProperty("chat").GetProperty("id").GetInt64() == admin;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException) { return false; }
    }
}
