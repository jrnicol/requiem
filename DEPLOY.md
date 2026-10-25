# Deploying Requiem to AWS

The infrastructure is one CloudFormation template, [`infra/aws/requiem.yaml`](infra/aws/requiem.yaml). GitHub Actions ([`.github/workflows/deploy.yml`](.github/workflows/deploy.yml)) builds and tests every push. On `main` it also publishes the app as a container image to `ghcr.io/jrnicol/requiem`, tagged with the commit and `latest`. The server checks for a new `latest` every two minutes and swaps it in.

**Why pull-based:** the AWS project's guardrails block IAM OpenID Connect providers, so GitHub Actions can't sign in to AWS. GitHub holds no AWS credentials at all.

**What it creates** (decided 2026-10-25). Region us-east-2, the project's Region. Costs are rough monthly figures, covered by the free plan's credit.

| Resource | Notes | Cost |
| --- | --- | --- |
| **EC2 instance** (t3.micro, Amazon Linux 2023) | Runs the app's container under Docker. Always on, so there's no cold start. One instance only: SQLite allows one writer. | ~$7.60 |
| **Data volume** (EBS gp3, 4 GiB, encrypted) | The SQLite database and the settings file. Separate from the instance, so the instance can be replaced without losing data. Deleting the stack snapshots it first. | ~$0.35 |
| Root volume (gp3, 10 GiB, encrypted) | The operating system and Docker images. | ~$0.80 |
| **Elastic IP** | A fixed address, so CloudFront's origin name never changes. | ~$3.65 |
| **CloudFront** | HTTPS at a `*.cloudfront.net` address. Pages and API calls pass through uncached; only `/_framework/*` is cached. | Free within 1 TB/month |
| Security group | Port 80 from CloudFront's addresses only. No SSH: use Session Manager. | Free |
| **AWS Backup** | Snapshots the data volume daily at 09:00 UTC and keeps 14 days. The vault is kept if the stack is deleted. | Cents |
| Systems Manager | Session Manager shell access, plus OS security patches every Sunday 10:00 UTC (reboots if needed). | Free |
| CloudWatch Logs | The app's output, kept 30 days. | Free within 5 GB |

That's about **$13 a month**. The project is on AWS's free plan, with $100 of credit until 2027-04-25. After that, or when the credit runs out, the project has to be upgraded to the paid plan in AWS Settings, or it closes.

---

## First-time setup

You need the AWS CLI signed in to the project (`aws login --region us-east-2`). These commands are for PowerShell, from the repository root.

### 1. Create the stack

Look up CloudFront's address list for the Region. Only those addresses may reach the server:

```powershell
$prefixList = aws ec2 describe-managed-prefix-lists --region us-east-2 --filters Name=prefix-list-name,Values=com.amazonaws.global.cloudfront.origin-facing --query "PrefixLists[0].PrefixListId" --output text
```

```powershell
aws cloudformation deploy --region us-east-2 --stack-name requiem --template-file infra/aws/requiem.yaml --capabilities CAPABILITY_IAM --parameter-overrides CloudFrontPrefixListId=$prefixList
```

This takes about five minutes, most of it CloudFront. On first boot, the server installs Docker, formats the data volume, generates a random **setup code**, and starts the app. The app migrates the database and seeds the catalog when it starts.

### 2. Get the address and setup code

```powershell
aws cloudformation describe-stacks --region us-east-2 --stack-name requiem --query "Stacks[0].Outputs" --output table
```

Keep `SiteUrl` and `InstanceId`. To read the setup code without opening a shell:

```powershell
$command = aws ssm send-command --region us-east-2 --instance-ids <InstanceId> --document-name AWS-RunShellScript --parameters 'commands=["grep SetupCode /var/lib/requiem/config/requiem.env"]' --query Command.CommandId --output text
```

```powershell
aws ssm get-command-invocation --region us-east-2 --instance-id <InstanceId> --command-id $command --query StandardOutputContent --output text
```

### 3. Create the first account

Open `SiteUrl`. If you get a 502 or 504, the server is still booting; wait a minute. You'll be sent to `/setup`. Enter the setup code and create your account. It becomes the site admin, which edits the shared powers catalog; everyone else signs up at `/register`.

Setup locks itself as soon as an account exists, so the code is useless from then on.

---

## Day to day

- **Deploying:** push to `main`. Once the workflow has published the image, the server picks it up within two minutes.
  - If the new version doesn't answer `/healthz` within three minutes, the server puts the previous one back and doesn't retry that image.
  - The app migrates the database when it starts. A rollback after a migration has run may not work with the newer schema.
- **Deploying or rolling back to a specific commit:** run `deploy.sh` with the full commit SHA:

  ```powershell
  aws ssm send-command --region us-east-2 --instance-ids <InstanceId> --document-name AWS-RunShellScript --parameters 'commands=["/opt/requiem/deploy.sh <full commit SHA>"]'
  ```

  The two-minute check moves it back to `latest` only when a newer `latest` is published.
- **Logs:** CloudWatch log group `/requiem/app`, or live:

  ```powershell
  aws logs tail /requiem/app --region us-east-2 --follow
  ```

- **A shell on the server:** `aws ssm start-session --region us-east-2 --target <InstanceId>`. This needs the [Session Manager plugin](https://docs.aws.amazon.com/systems-manager/latest/userguide/session-manager-working-with-install-plugin.html). The console's **EC2 > Instances > Connect > Session Manager** works too.
  - The app is the `requiem` Docker container.
  - Its data is in `/var/lib/requiem`.
  - The update check is `requiem-update.timer`.
- **Changing infrastructure:** edit `infra/aws/requiem.yaml` and rerun step 1's `deploy` command.
  - The first-boot script runs only once per instance, so changes to it take effect only on a new instance.
  - To get a new instance, change something that replaces it, such as `InstanceType` or the AMI.
  - The data volume and its contents carry over to the new instance.
- **Backups:** **AWS Backup > Backup vaults > requiem-backups**.
  - To restore, restore a recovery point to a new volume in the same Availability Zone.
  - Then, in a Session Manager shell, stop the app (`docker stop requiem`) and copy `requiem.db` over from the restored volume.

## If something fails

| Symptom | Meaning | Fix |
| --- | --- | --- |
| 502/504 from CloudFront | The app isn't answering on the server. During the first five minutes, it's still booting. | Check the logs. On the server, `docker ps -a` and `docker logs requiem`. |
| Nothing deploys after a push | The workflow didn't publish, or the image failed its health check. | Check the Actions run. On the server, `journalctl -u requiem-update` shows each check, and `/opt/requiem/failed-image` names an image that was rolled back. |
| First boot failed | The setup script stopped with an error. | `sudo cat /var/log/cloud-init-output.log` in a Session Manager shell. |
| `AccessDenied` on actions that used to work | The project may be near a spend limit, which blocks creating resources. | Check **AWS Settings > Billing**. |

---

## Retiring the Azure deployment

Until 2026-10-25 Requiem ran on Azure Container Apps and Azure SQL, from [`infra/main.bicep`](infra/main.bicep). The workflow no longer deploys there, so the Azure site keeps running the last version it got. Its data isn't carried over: the AWS site starts fresh.

To shut it down, delete its resource group in [Azure Cloud Shell](https://shell.azure.com):

```bash
az group delete --name rg-requiem --yes
```

Then remove the repository variables `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, `AZURE_RESOURCE_GROUP` and `AZURE_CONTAINERAPP_NAME` (**Settings > Secrets and variables > Actions > Variables**). After that, `infra/main.bicep`, `infra/main.bicepparam` and the Azure Monitor package can be deleted.
