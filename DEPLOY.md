# Deploying Requiem to AWS

Requiem runs serverless in the AWS project's Region, us-east-2 ([SERVERLESS.md](SERVERLESS.md) has the design). Two CloudFormation stacks:

| Stack | Template | What it holds | Who changes it |
| --- | --- | --- | --- |
| `requiem-pipeline` | [`infra/aws/pipeline.yaml`](infra/aws/pipeline.yaml) | The GitHub connection, a CodeBuild project, a bucket for the API's packages, and the role CloudFormation deploys with | You, once by hand (and again only if the pipeline itself changes) |
| `requiem-app` | [`infra/aws/app.yaml`](infra/aws/app.yaml) | The DynamoDB table, the browser app's S3 bucket, CloudFront, the API Gateway HTTP API and the Lambda function | The pipeline, on every push to `main` |

**Each push to `main`:**
1. CodeBuild runs [`buildspec.yml`](buildspec.yml), which runs the tests against DynamoDB Local.
2. It publishes the API for Lambda (arm64) and updates `requiem-app` with the new package.
3. It uploads the browser app to S3 and clears CloudFront's cached copy.

A failed test stops the deploy. GitHub holds no AWS credentials: the project's guardrails block GitHub's OpenID Connect sign-in, so AWS pulls from GitHub through CodeConnections instead.

**Cost:** about $0–1 a month at this scale.
- DynamoDB, Lambda and CloudFront stay within AWS's always-free allowances.
- API Gateway is free for 12 months, then about $1 per million requests.
- CodeBuild's small machines cost about half a cent per build minute, and a build takes a few minutes.

The project is on AWS's free plan with $100 of credit until 2027-04-25. After that the project has to be upgraded to the paid plan in AWS Settings, or it closes.

---

## First-time setup

You need the AWS CLI signed in to the project (`aws login --region us-east-2`). These commands are for PowerShell, from the repository root.

### 1. Create the pipeline

```powershell
aws cloudformation deploy --region us-east-2 --stack-name requiem-pipeline --template-file infra/aws/pipeline.yaml --capabilities CAPABILITY_IAM
```

### 2. Approve the GitHub connection (once)

CloudFormation creates the connection as **Pending**. Only a person can approve it:

1. In the AWS console, open **Developer Tools > Settings > Connections**, choose **requiem-pipeline-github**, then **Update pending connection**.
2. Sign in to GitHub if asked. Install the **AWS Connector for GitHub** app on the `jrnicol` account, limited to the `requiem` repository.
3. Back in the console, choose **Connect**. The status turns **Available**.

### 3. First deploy

Push to `main`, or start a build by hand:

```powershell
aws codebuild start-build --region us-east-2 --project-name <BuildProject output of requiem-pipeline>
```

The first build creates `requiem-app` and takes 10–15 minutes, most of it CloudFront. Later builds take about five. To watch it, open **CodeBuild > Build projects** in the console, or tail the log:

```powershell
aws logs tail /requiem-pipeline/build --region us-east-2 --follow
```

### 4. Create the first account

Get the address, then the one-time setup code:

```powershell
aws cloudformation describe-stacks --region us-east-2 --stack-name requiem-app --query "Stacks[0].Outputs" --output table
```

```powershell
aws secretsmanager get-secret-value --region us-east-2 --secret-id <SetupCodeSecret output> --query SecretString --output text
```

Open `SiteUrl`, which sends you to `/setup`, and enter the code. The account becomes the site admin, which edits the shared powers catalog; everyone else signs up at `/register`. Setup locks itself once an account exists.

---

## Day to day

- **Deploying:** push to `main`. If the build fails, nothing changes in AWS.
- **Logs:**
  - The API: `aws logs tail /requiem-app/api --region us-east-2 --follow`
  - Builds: `/requiem-pipeline/build`
- **Cold starts:** after a quiet spell, the first API call takes a few seconds while Lambda starts .NET. Raising `MemorySize` in `app.yaml` shortens it.
- **Data:**
  - The table is kept even if the stack is deleted, and has deletion protection.
  - Point-in-time recovery can restore it to any second in the last 35 days, into a new table: **DynamoDB > Tables > Backups**.
- **Changing infrastructure:**
  - Edit `infra/aws/app.yaml` and push; the pipeline applies it.
  - Edit `infra/aws/pipeline.yaml` and rerun step 1 by hand.

## If something fails

| Symptom | Meaning | Fix |
| --- | --- | --- |
| Builds never start after a push | The GitHub connection isn't approved yet, or the webhook wasn't created. | Step 2. If the connection is **Available** but builds still don't start, start one by hand (step 3); updating the pipeline stack recreates the webhook. |
| Build fails in `dotnet test` | A test failed; nothing was deployed. | Read the build log. |
| `AccessDenied` in the build's deploy step | `app.yaml` uses something the deploy role (`pipeline.yaml`) doesn't allow. | Add it to `DeployRole` and rerun step 1. |
| 502 from `/api/...` | The Lambda function failed to start. | Check the API log; a missing setting or a startup exception shows there. |
| `AccessDenied` on actions that used to work | The project may be near a spend limit, which blocks creating resources. | Check **AWS Settings > Billing**. |

---

## Retiring the Azure deployment

Until 2026-10-25 Requiem ran on Azure Container Apps and Azure SQL, from [`infra/main.bicep`](infra/main.bicep). The workflow no longer deploys there, so the Azure site keeps running the last version it got. Its data isn't carried over: the AWS site starts fresh.

To shut it down, delete its resource group in [Azure Cloud Shell](https://shell.azure.com):

```bash
az group delete --name rg-requiem --yes
```

Then remove the repository variables `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, `AZURE_RESOURCE_GROUP` and `AZURE_CONTAINERAPP_NAME` (**Settings > Secrets and variables > Actions > Variables**). After that, `infra/main.bicep`, `infra/main.bicepparam` and the Azure Monitor package can be deleted.
