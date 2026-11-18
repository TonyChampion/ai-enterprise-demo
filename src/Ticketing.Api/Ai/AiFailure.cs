using System.ClientModel;

namespace Ticketing.Api.Ai;

public enum AiFailureKind
{
    /// <summary>429: over the token limit. Wait and retry; the work is not lost.</summary>
    RateLimited,
    /// <summary>403 from the gateway's content safety check. Goes to a person.</summary>
    SafetyBlocked,
    /// <summary>The model deployment's own content filter refused it (400, content_filter).</summary>
    ModelFiltered,
    /// <summary>401/403 that is not safety: bad key, local auth off, no role, token quota.</summary>
    Denied,
    /// <summary>Our timeout fired.</summary>
    Timeout,
    /// <summary>Anything else: network, 5xx.</summary>
    Unavailable
}

public record AiFailure(AiFailureKind Kind, int? Status, string Detail, TimeSpan? RetryAfter)
{
    /// <summary>
    /// 403 is not only content safety: a token quota and an access failure are 403s too.
    /// Read the body before deciding what to do.
    /// </summary>
    public static AiFailure From(Exception exception)
    {
        for (var e = exception; e is not null; e = e.InnerException)
        {
            if (e is ClientResultException cre)
            {
                var raw = cre.GetRawResponse();
                var body = SafeBody(raw);
                TimeSpan? retryAfter = null;
                if (raw is not null && raw.Headers.TryGetValue("Retry-After", out var ra) && int.TryParse(ra, out var secs))
                    retryAfter = TimeSpan.FromSeconds(secs);

                var kind = cre.Status switch
                {
                    429 => AiFailureKind.RateLimited,
                    // The gateway's content safety block comes back as a 403 with an empty body.
                    // Access problems (no role, local auth off) come with a body that says so.
                    403 when LooksLikeSafety(body) || string.IsNullOrWhiteSpace(body) => AiFailureKind.SafetyBlocked,
                    400 when body.Contains("content_filter", StringComparison.OrdinalIgnoreCase) => AiFailureKind.ModelFiltered,
                    401 or 403 => AiFailureKind.Denied,
                    _ => AiFailureKind.Unavailable
                };
                return new AiFailure(kind, cre.Status, Trim(body.Length > 0 ? body : cre.Message), retryAfter);
            }
            if (e is TimeoutException || e is OperationCanceledException)
                return new AiFailure(AiFailureKind.Timeout, null, "The model did not answer within our timeout.", null);
        }
        return new AiFailure(AiFailureKind.Unavailable, null, Trim(exception.Message), null);
    }

    private static bool LooksLikeSafety(string body) =>
        body.Contains("safety", StringComparison.OrdinalIgnoreCase) ||
        body.Contains("shield", StringComparison.OrdinalIgnoreCase) ||
        body.Contains("attack", StringComparison.OrdinalIgnoreCase) ||
        body.Contains("jailbreak", StringComparison.OrdinalIgnoreCase);

    private static string SafeBody(System.ClientModel.Primitives.PipelineResponse? raw)
    {
        try { return raw?.Content?.ToString() ?? ""; } catch { return ""; }
    }

    private static string Trim(string s) => s.Length <= 400 ? s : s[..400] + "...";
}
