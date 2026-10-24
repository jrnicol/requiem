# Deploying Requiem to Azure

The infrastructure is in [`infra/main.bicep`](infra/main.bicep); GitHub Actions ([`.github/workflows/deploy.yml`](.github/workflows/deploy.yml)) builds, tests and deploys every push to `main`.

**What it creates** (region West US 2, free tier; decided 2026-10-24):

| Resource | Notes | Cost |
| --- | --- | --- |
| App Service plan **F1** (Linux) + web app | .NET 10. Free tier sleeps when idle and allows 60 CPU-minutes a day, so the first load after a quiet spell is slow. | Free |
| Azure SQL database (serverless, **free offer**) | Pauses when the monthly free allowance runs out rather than billing. Auto-pauses after an hour idle; waking takes up to a minute. One free database offer per subscription. | Free |
| Two managed identities | The app's (signs in to SQL and Key Vault, no passwords) and the deploy identity (GitHub Actions signs in with OpenID Connect, no stored secrets). | Free |
| Key Vault | Holds the first-run setup code. | Pennies |
| Log Analytics + Application Insights | Errors and performance. Ingestion is capped to stay in the free allowance. | Free |

To leave the free tier later, set `appServiceSku` in [`infra/main.bicepparam`](infra/main.bicepparam) to `B1` (about US$13/month, always on) and redeploy.

---

## First-time setup

You need an Azure subscription where you're **Owner** (the template grants roles to the two identities).

### 1. Create the resources (Azure Cloud Shell)

Open **https://shell.azure.com** and choose **Bash**. Cloud Shell already has the Azure CLI and Bicep. Then run:

```bash
git clone https://github.com/jrnicol/requiem.git && cd requiem
```

```bash
az group create --name rg-requiem --location westus2
```

You become the database's administrator, and a random setup code is generated. **Copy the setup code it prints:** you need it in step 5.

```bash
export REQUIEM_SQL_ADMIN_OBJECT_ID=$(az ad signed-in-user show --query id -o tsv)
export REQUIEM_SQL_ADMIN_LOGIN=$(az ad signed-in-user show --query userPrincipalName -o tsv)
export REQUIEM_SETUP_CODE=$(openssl rand -base64 18)
echo "Setup code: $REQUIEM_SETUP_CODE"
```

```bash
az deployment group create --resource-group rg-requiem --template-file infra/main.bicep --parameters infra/main.bicepparam --query properties.outputs
```

Keep the outputs. `webAppName`, `deployClientId`, `tenantId` and `subscriptionId` are used in step 3.

If the deployment fails on the database with a message about the free offer, your subscription has already used it. Ask and the template can switch to a small paid database instead.

### 2. Let the app into its database (once)

The template makes you the database's administrator. The app's own identity needs a database user, which only an administrator can create:

1. In the Azure portal, open **SQL databases > requiem > Query editor**.
2. Sign in with **Microsoft Entra authentication**. If it offers to add a firewall rule for your IP address, allow it.
3. Run:

```sql
CREATE USER [id-requiem-app] FROM EXTERNAL PROVIDER;
ALTER ROLE db_owner ADD MEMBER [id-requiem-app];
```

`db_owner` is needed because the app applies database migrations itself when it starts.

### 3. Connect GitHub to Azure

In the GitHub repository, open **Settings > Secrets and variables > Actions > Variables**. Add these **repository variables** (they aren't secrets, so use Variables, not Secrets):

| Variable | Value from step 1's outputs |
| --- | --- |
| `AZURE_CLIENT_ID` | `deployClientId` |
| `AZURE_TENANT_ID` | `tenantId` |
| `AZURE_SUBSCRIPTION_ID` | `subscriptionId` |
| `AZURE_WEBAPP_NAME` | `webAppName` |

### 4. Deploy

Open **Actions > Build and deploy > Run workflow**, or push to `main`. The workflow:
1. runs the tests;
2. publishes the app;
3. deploys it to the **production** environment.

GitHub creates that environment the first time. On start, the app migrates the database and seeds the catalog.

### 5. Create the first GM

Open the `webAppUrl` from step 1. You'll be sent to `/setup`. Enter the **setup code** from step 1 and create your GM account.

Afterwards, delete the code so setup can't be reached again:
1. Open **Key vaults > kv-requiem-… > Secrets > Auth--SetupCode** and delete it.
2. Restart the web app.

---

## Day to day

- **Deploying:** push to `main`, or run the workflow by hand.
- **Changing infrastructure:** edit `infra/main.bicep`, then rerun the `az deployment group create` command from step 1 in Cloud Shell. Set the three `export` lines again first, reusing your setup code if you still need it.
- **Watching it:** open the web app's **Log stream** for live logs, and **Application Insights > Failures** for errors.
- **The first request is slow, or a sign-in fails after a quiet spell:** the free tier woke the app, or the database was paused. Wait a minute and retry.
