# Serverless rebuild: S3 + CloudFront, API Gateway + Lambda, DynamoDB

Decided with the user on 2026-10-25, replacing the EC2 + SQLite plan of the same day (stack deleted). AWS project Region: us-east-2. This is the design and the order of work; [DEPLOY.md](DEPLOY.md) is rewritten in the last phase.

## Shape

```
browser ── CloudFront (https://…cloudfront.net)
             ├─ /*      → S3 bucket (private, Origin Access Control): the Blazor WebAssembly app
             └─ /api/*  → API Gateway HTTP API → one Lambda (dotnet10, arm64): the existing ASP.NET Core API
                                                       └─ DynamoDB (one table, on-demand)
```

- **One origin.** CloudFront serves both the app and `/api/*`, so the SameSite=Strict sign-in cookie keeps working. There are no CORS rules or tokens in the browser.
- **One Lambda for the whole API.** The minimal-API endpoints run unchanged under `Amazon.Lambda.AspNetCoreServer.Hosting` (`AddAWSLambdaHosting(LambdaEventSource.HttpApi)`). Splitting the API into a function per endpoint would add deploy and cold-start cost for no gain at this size.
  - SnapStart (supported for `dotnet10`) cuts cold starts.
- **The client becomes a standalone Blazor WebAssembly app.** Today the server renders the page shell (`App.razor`). On S3 the shell is a static `index.html`.
  - Deep links like `/play/{id}` fall back to `index.html` through a CloudFront Function.
- **Cost:** about $0–1 a month at this scale.
  - DynamoDB (25 GB), Lambda (1M requests) and CloudFront (1 TB) stay inside AWS's always-free allowances.
  - API Gateway's free allowance (1M requests) lasts 12 months, and after that it's about $1 per million requests.

## DynamoDB: one table

Partition key `PK` and sort key `SK`, both strings, plus two global secondary indexes (`GSI1`, `GSI2`) with the same shape. On-demand capacity, with point-in-time recovery turned on.

| Item | PK | SK | Indexes | Notes |
| --- | --- | --- | --- | --- |
| Catalog entry | `CATALOG` | `POWER#<id>`, `BLOODLINE#<id>`, … | | The whole catalog (about 390 items) loads with one Query. Lambda caches it, checked against the version stamp below. Integer IDs stay, because characters refer to them. |
| Catalog version | `CATALOG` | `VERSION` | | Bumped on every admin edit, so caches refresh. Also the counter for new catalog IDs. |
| User | `USER#<id>` | `USER` | | ASP.NET Identity fields, plus roles as a list. |
| Email lookup | `EMAIL#<normalized>` | `EMAIL` | | Written in the same transaction as the user, with a condition that it doesn't exist yet, so emails stay unique. |
| Campaign | `CAMPAIGN#<id>` | `CAMPAIGN` | | |
| Member | `CAMPAIGN#<id>` | `MEMBER#<userId>` | GSI1: `USER#<userId>` / `CAMPAIGN#<id>` | GSI1 lists a user's campaigns. |
| Invite | `CAMPAIGN#<id>` | `INVITE#<id>` | GSI1: `INVITE#<code>` / `INVITE` | GSI1 finds an invite by its code. |
| Character | `CHARACTER#<id>` | `CHARACTER` | GSI2: `CAMPAIGN#<id>` / `CHARACTER#<id>` | **One document per character**: the sheet and all its lists (attributes, skills, disciplines, wounds, XP ledger, links and so on). It's loaded and saved whole, as every endpoint already does. Summary fields sit beside it for the campaign listing, which uses GSI2 and doesn't read the documents. Writes check `Version` for optimistic concurrency. |
| Scene | `CAMPAIGN#<id>` | `SCENE#<id>` | | Includes the member character IDs in order. |
| Audit entry | `CHARACTER#<id>` or `CAMPAIGN#<id>` | `AUDIT#<ULID>` | | The ULID keeps entries in time order. Undo marks the entry. Saving a change and its audit entry together is a `TransactWriteItems`, which replaces `SaveWithAuditAsync`. |
| Data-protection key | `DATAPROTECTION` | `KEY#<name>` | | Sign-in cookie keys, so a cold start doesn't sign anyone out. |

Everything a request needs is a GetItem, a Query or a transaction. Nothing scans the table. Cross-character questions, such as who names this character as sire before deleting it, read the campaign's characters through GSI2. Campaigns are small, so that's fine.

## Code changes

- **Storage interfaces** in `Requiem.Data`: catalog, characters, campaigns (with members and invites), scenes, audit, plus ASP.NET Identity's user and role stores and a data-protection key repository.
  - The entities stay as plain classes.
  - EF Core, its configurations and both migrations projects (`Requiem.Data.Sqlite`, `Requiem.Data.SqlServer`) are deleted at the end.
- **Endpoints and `CampaignAccess`** move from LINQ on `RequiemDbContext` (about 130 call sites in 12 files) to those interfaces.
- **Audit entry IDs** change from `long` to a ULID string, which affects the API contract and the client's undo calls.
- **Rate limiting** is in-memory today, so on Lambda each running copy keeps its own counts. API Gateway throttling at the stage covers floods. Sign-up limits per IP become a small DynamoDB counter with a TTL.
- **Local development and tests** run against DynamoDB Local. Each test class gets its own table, and the existing API tests are the safety net for the rewrite.

## Deploying

The project's guardrails block GitHub OIDC (an undocumented explicit deny). They do allow IAM users, CodeBuild and CodeConnections (checked 2026-10-25).

- **Infrastructure:** one CloudFormation template creates the table, bucket, distribution, HTTP API, function and roles. Like the earlier EC2 template, it gets `cfn-lint`, per-resource design notes, and a retain policy on the table.
- **Pipeline (decided with the user):** AWS CodeBuild, connected to GitHub through CodeConnections, builds, tests and deploys on every push to `main`.
  - No AWS keys are stored in GitHub.
  - The connection needs one approval in the AWS console.

**Local database (decided with the user):** DynamoDB Local on Amazon Corretto 21 (Java), for development and the tests.

## Phases

1. **Interfaces with the current implementation.** Endpoints move onto the storage interfaces, still backed by EF Core and SQLite. All tests stay green. This is the riskiest refactor, done while the database underneath is still the familiar one.
2. **DynamoDB implementation.** Tables, stores, Identity, data-protection keys and catalog seeding, with the tests switched to DynamoDB Local. When everything passes, EF Core is deleted.
3. **Hosting split.** The client becomes standalone WebAssembly, and the API gets Lambda hosting. Locally, the app still runs as one site for development.
4. **Infrastructure and pipeline.** The CloudFormation template, then the deploy pipeline.
5. **Cut-over.** First deploy and checks in the browser. The EC2 template (`infra/aws/requiem.yaml`), the Azure files and Azure itself get retired.
