# Architecting AI-Ready Enterprises on Azure: demo code

The code from **EAH03, VS Live Orlando 2026**. A support ticketing API with two AI features, triage and chat, built so they can live inside an enterprise security boundary:

- **Identity, not keys.** `DefaultAzureCredential` everywhere, and local auth turned off on the model resource.
- **One front door.** API Management as the AI gateway: token validation, per-caller token limits, Prompt Shields, and token metrics.
- **Injection contained by design.** Rules separate from data, typed output, and application rules that hold when the model is fooled.
- **Access decided by code.** Chat tools take identity from the token, never from the model.
- **Seeing it.** OpenTelemetry GenAI spans in Application Insights, tagged with `caller` and `ticket.id`, and the queries that answer "who called it, about what, and how much".

The ticketing API is a light version of the one from the **Building Production-Ready Modern APIs with .NET** hands-on lab: same domain, with most of the production hardening taken out to keep the focus on AI.

## What's here

| Path | What it is |
|---|---|
| `infra/main.bicep` | Foundry (project + `triage` and `chat` deployments), Content Safety, API Management (Basic v2), Log Analytics, Application Insights, role assignments |
| `infra/gateway-policy.xml` | The gateway policy: `validate-azure-ad-token`, `llm-token-limit`, `llm-content-safety`, `llm-emit-token-metric` |
| `src/Ticketing.Api/Ai` | Model clients for each route, the `IChatClient` pipeline, failure classification, telemetry tags |
| `src/Ticketing.Api/Features/Triage` | Typed triage, the naive and hardened prompts, and the application rules |
| `src/Ticketing.Api/Features/Chat` | Chat over Server-Sent Events, the safe tools, and the naive "confused deputy" tool |
| `tests/` | Fast tests of the triage rules against a fooled model, plus the injection eval suite (real model, opt-in) |
| `tools/RogueCaller` | A second app that floods the gateway |
| `queries/` | KQL for one ticket's trace, gateway denials, tool calls, and token usage by caller |

## Run it

You need the .NET 10 SDK, the Azure CLI, and an Azure subscription.

```powershell
az login
./infra/deploy.ps1 -PublisherEmail you@example.com      # about 10-15 minutes, mostly API Management
./make-tokens.ps1                                        # dev JWTs for alice (Contoso), bob (Fabrikam), dana (agent)
dotnet run --project src/Ticketing.Api --launch-profile https
```

`deploy.ps1` writes `src/Ticketing.Api/appsettings.Local.json` (gitignored). Role assignments can take a few minutes to take effect after the deployment finishes.

The demo page at `https://localhost:7001` has switches for each stage of the talk:

| Switch | Options | Demo |
|---|---|---|
| Model | Key → Identity → Gateway | 1: from the demo version to the AI-ready version |
| Triage | Naive / Hardened | 2: poisoned ticket 4471 |
| Tools | Safe / Naive | 3: ask for someone else's tickets |
| Chat | Streamed / Buffered | 1: first words, and cancelling when the tab closes |

Turn key auth off and on with `./infra/local-auth.ps1 -Disable` / `-Enable`.

### Tests

```powershell
dotnet test                                             # rules tests, no model needed
$env:RUN_AI_EVALS = "1"; dotnet test --filter TriageEvals   # the injection eval suite, calls the real model
```

## Tear it down

API Management and the model deployments cost money while idle.

```powershell
az group delete -n rg-aiready-demo --yes --no-wait
```

## Notes

- **The switches are a demo convenience.** In a real app there's one route (the gateway), one prompt (hardened) and one set of tools (safe). The demo endpoints only exist in Development.
- **Running locally, everything authenticates as you.** The ticketing API and the RogueCaller share one gateway token counter. In Azure each app's managed identity gets its own.
- **Prompt Shields is a signal, not a guarantee.** Test it against your own attacks. The model deployment's own Prompt Shields are set to annotate, not block, so the demo can show the gateway doing the blocking. In production you'd want both on.
- **Token totals are usage, not your bill.** The cost line in `tuesday-question.kql` is an estimate using example rates.
- `Azure.AI.OpenAI` is pinned to a beta (2.9.0-beta.1) to line up with the `OpenAI` library that `Microsoft.Extensions.AI.OpenAI` 11 needs.
