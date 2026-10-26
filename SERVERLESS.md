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
| Catalog entry | `CATALOG` | `POWER#00042`, `BLOODLINE#00003`, … | | The whole catalog (about 390 items) loads with one Query. Each process caches it and rechecks the version stamp every 30 seconds. Integer IDs stay, because characters refer to them. |
| Catalog version | `CATALOG` | `VERSION` | | `Version` is bumped on every admin edit, so caches reload. `SeedHash` fingerprints the seed files, so startup seeding is skipped while they're unchanged. |
| User | `USER#<id>` | `USER` | | ASP.NET Identity's fields as a document, `ConcurrencyStamp` checked on update, and role names in `Roles`. |
| User name / email lookup | `USERNAME#<normalized>`, `EMAIL#<normalized>` | `LOOKUP` | | Point at the user. They're written in the user's transaction with a not-exists condition, so both stay unique. |
| User count | `USERCOUNT` | `USERCOUNT` | | Answers "has the site been set up?". |
| Role | `ROLE#<normalized>` | `ROLE` | | Just Admin. |
| Campaign | `CAMPAIGN#<id>` | `CAMPAIGN` | | |
| Member | `CAMPAIGN#<id>` | `MEMBER#<userId>` | GSI1: `USER#<userId>` / `CAMPAIGN#<id>` | GSI1 lists a user's campaigns. Access checks for one campaign read the member item directly, because the index can lag a just-made membership. |
| Invite | `CAMPAIGN#<id>` | `INVITE#<id>` | GSI1: `INVITE#<token hash>` / `INVITE` | Plain attributes, so joining can test them. The join spends a use (if limited) and adds the member in one transaction, on condition the link is live. |
| Character | `CHARACTER#<id>` | `CHARACTER` | GSI2: `CAMPAIGN#<id>` / `CHARACTER#<id>` | **One document per character**: the sheet and all its lists (attributes, skills, disciplines, wounds, XP ledger, links and so on). It's loaded and saved whole, as every endpoint already does. Writes check `Version` for optimistic concurrency. New list items get the next free id in their list. |
| Scene | `SCENE#<id>` | `SCENE` | GSI2: `CAMPAIGN#<id>` / `SCENE#<id>` | Includes the member character IDs in order. |
| Audit entry | `AUDIT#<id>` | `AUDIT` | GSI1: `CHARACTER#<id>` / `AUDIT#<id>` | The id is time-ordered, so a character's history is GSI1 newest first. Undo marks the entry. A change and its audit entries commit in one transaction. |
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
- **Pipeline (decided with the user):** AWS CodeBuild builds, tests and deploys on every push to `main`. It runs inside a CodePipeline, because CodeBuild alone can't create the GitHub webhook through a CodeConnections GitHub-app connection (found 2026-10-25); the pipeline gets pushes from the connection itself.
  - No AWS keys are stored in GitHub.
  - The connection needs one approval in the AWS console.

**Local database (decided with the user):** DynamoDB Local on Amazon Corretto 21 (Java), for development and the tests.

## Phases

1. **Interfaces with the current implementation (done 2026-10-25).** Endpoints move onto the storage interfaces, still backed by EF Core and SQLite. All tests stay green. This is the riskiest refactor, done while the database underneath is still the familiar one.
2. **DynamoDB implementation (done 2026-10-25).** Stores, Identity, data-protection keys and catalog seeding on DynamoDB; the tests run against DynamoDB Local. EF Core, both migrations projects and the SQLite/SQL Server providers are gone. So are blood bonds and boons, which had no screens yet, and the legacy-data upgrade, since the data starts fresh.
3. **Hosting split (done 2026-10-25).** The client is a standalone WebAssembly app with its own `wwwroot/index.html`. The API has no server-rendered pages, answers errors as problem details, and gets Lambda hosting (`AddAWSLambdaHosting`, HTTP API events). Locally, and in tests, it also serves the client and gives app paths `index.html` (`Hosting:ServeClient`, off on Lambda). Notes for phase 4:
   - Only the `_framework` files have fingerprinted names (`app.css` and MudBlazor's files don't), so the S3 upload caches `_framework/*` for a year and everything else briefly, `index.html` not at all.
   - The client publishes to about 22 MB (3.8 MB as served, brotli). The API's Lambda publish (`-r linux-arm64`) is 52 MB because it carries a copy of the client; trimming that is optional.
4. **Infrastructure and pipeline (deployed 2026-10-25: https://d1amgeq01zslzq.cloudfront.net).** [`infra/aws/app.yaml`](infra/aws/app.yaml) holds the app: the table (deletion-protected, point-in-time recovery), the site bucket (CloudFront only, through Origin Access Control), CloudFront with a function that gives app paths `index.html`, the HTTP API (throttled) and the Lambda function (dotnet10, arm64). Its setup code is a generated Secrets Manager secret. [`infra/aws/pipeline.yaml`](infra/aws/pipeline.yaml) holds the GitHub connection, the CodeBuild project ([`buildspec.yml`](buildspec.yml): test, publish, deploy, upload) and a role only CloudFormation can use to deploy the app. SnapStart is left for later: plain CloudFormation would need a new function version per deploy.
- First runs: a cold start (including seeding the catalog) took about 2.4 s at 1 GB, using 156 MB; a full deploy takes about 4 minutes once the app stack exists.   - The push trigger didn't start a run for the first push after the pipeline was fixed (its repository setting was wrong when it was created). Runs were started by hand with `aws codepipeline start-pipeline-execution`; whether pushes trigger runs is still to be checked.
5. **Cut-over.** First deploy and checks in the browser. The EC2 template (`infra/aws/requiem.yaml`), the Azure files and Azure itself get retired.
