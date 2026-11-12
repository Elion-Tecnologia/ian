using Ian.Application.Telegram;
using Ian.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ian.Infrastructure.Persistence;

public sealed class PostgresTelegramReplyOutbox(IanDbContext db) : ITelegramReplyOutbox
{
    public async Task<TelegramReplyClaim?> ClaimAsync(DateTimeOffset now, long? updateId, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Serializes only short claim/finalization transactions, never HTTP or model calls.
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(510, 1)", ct);
        var interrupted = await db.Set<TelegramAgentTurn>().Where(t => t.SendState == "Sending" &&
            (t.SendLeaseUntilUtc == null || t.SendLeaseUntilUtc <= now)).ToListAsync(ct);
        foreach (var turn in interrupted)
        {
            turn.SendState = "Uncertain";
            turn.SendStatus = "Interrupted";
            turn.SendLeaseUntilUtc = null;
            turn.NextSendAttemptAtUtc = null;
            var attempt = await db.Set<TelegramReplyAttempt>().SingleOrDefaultAsync(a => a.UpdateId == turn.UpdateId && a.Number == turn.SendAttempts, ct);
            if (attempt is not null)
            {
                attempt.Status = "Interrupted";
                attempt.DeliveryUncertain = true;
                attempt.FinishedAtUtc = now;
            }
        }
        await db.SaveChangesAsync(ct);
        // Telegram's retry_after also applies to subsequent replies when this reply exhausted its budget.
        var rateLimit = await db.Set<TelegramReplyAttempt>().AsNoTracking()
            .Where(a => a.Status == "RateLimited" && a.RetryAfterSeconds > 0 && a.FinishedAtUtc != null)
            .OrderByDescending(a => a.FinishedAtUtc).FirstOrDefaultAsync(ct);
        if (rateLimit?.FinishedAtUtc is { } limitedAt && rateLimit.RetryAfterSeconds is { } seconds &&
            now - limitedAt < TimeSpan.FromSeconds(seconds))
        {
            await tx.CommitAsync(ct);
            return null;
        }
        // One administrator: an in-flight reply or an older retry_after also gates later replies.
        TelegramReplyClaim? claim = null;
        if (!await db.Set<TelegramAgentTurn>().AnyAsync(t => t.SendState == "Sending", ct))
        {
            var turn = await db.Set<TelegramAgentTurn>().Where(t => t.ResponseText != null &&
                (t.SendState == "Pending" || t.SendState == "Failed" && t.NextSendAttemptAtUtc != null))
                .OrderBy(t => t.UpdateId).FirstOrDefaultAsync(ct);
            if (turn is not null && (updateId is null || turn.UpdateId == updateId) &&
                (turn.NextSendAttemptAtUtc is null || turn.NextSendAttemptAtUtc <= now))
            {
                if (turn.SendAttempts >= TelegramReplyDeliveryPolicy.MaxAttempts)
                {
                    turn.SendState = "Failed";
                    turn.NextSendAttemptAtUtc = null;
                }
                else
                {
                    var payload = await db.TelegramUpdatesInbox.AsNoTracking().Where(u => u.UpdateId == turn.UpdateId).Select(u => u.PayloadJson).SingleAsync(ct);
                    var token = Guid.NewGuid();
                    turn.SendState = "Sending";
                    turn.SendAttempts++;
                    turn.SendToken = token;
                    turn.SendLeaseUntilUtc = now.Add(TelegramReplyDeliveryPolicy.LeaseDuration);
                    turn.NextSendAttemptAtUtc = null;
                    db.Add(new TelegramReplyAttempt { UpdateId = turn.UpdateId, Number = turn.SendAttempts, Token = token, StartedAtUtc = now });
                    claim = new(turn.UpdateId, turn.TelegramUserId, turn.ResponseText ?? "", payload, turn.SendAttempts, token);
                }
            }
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return claim;
    }

    public async Task<bool> FinishAsync(TelegramReplyClaim claim, TelegramTextSendResult result, DateTimeOffset now, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(510, 1)", ct);
        // A late definitive response may resolve recovery's Uncertain state, only for the same token.
        var turn = await db.Set<TelegramAgentTurn>().SingleOrDefaultAsync(t => t.UpdateId == claim.UpdateId &&
            t.SendToken == claim.Token && t.SendAttempts == claim.Number && (t.SendState == "Sending" || t.SendState == "Uncertain"), ct);
        if (turn is null) return false;
        var attempt = await db.Set<TelegramReplyAttempt>().SingleAsync(a => a.UpdateId == claim.UpdateId && a.Number == claim.Number && a.Token == claim.Token, ct);
        var decision = TelegramReplyDeliveryPolicy.Classify(result, claim.Number, now);
        turn.SendState = decision.State;
        turn.NextSendAttemptAtUtc = decision.Next;
        turn.TelegramMessageId = result.IsAccepted && result.MessageId is > 0 ? result.MessageId : null;
        turn.SendStatus = result.Status.ToString();
        turn.SendToken = null;
        turn.SendLeaseUntilUtc = null;
        attempt.FinishedAtUtc = now;
        attempt.Status = result.Status.ToString();
        attempt.TransportAttempts = result.Attempts;
        attempt.DeliveryUncertain = decision.State == "Uncertain";
        attempt.TelegramMessageId = turn.TelegramMessageId;
        attempt.RetryAfterSeconds = result.RetryAfter is { } delay ? (long)Math.Ceiling(delay.TotalSeconds) : null;
        attempt.NextAttemptAtUtc = decision.Next;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }
}
