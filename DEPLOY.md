# Deploying Requiem to Azure

The infrastructure is in [`infra/main.bicep`](infra/main.bicep). GitHub Actions ([`.github/workflows/deploy.yml`](.github/workflows/deploy.yml)) builds and tests every push. On `main` it also publishes the app as a container image to `ghcr.io/jrnicol/requiem` and rolls that image out to Azure.

**What it creates** (free tier, decided 2026-10-24). The default region is West US 3, because West US 2 refused new SQL servers for this subscription. Override it with `REQUIEM_LOCATION` and `REQUIEM_SQL_LOCATION`.

| Resource | Notes | Cost |
| --- | --- | --- |
| **Azure Container Apps** environment + app | Runs the app's container, one copy at most. Scales to zero when idle, so the first request after a quiet spell takes roughly 10–20 seconds. Needs no App Service quota, which Free Trial subscriptions don't have. | Free within the monthly allowance (hundreds of active hours) |
| Azure SQL database (serverless, **free offer**) | Pauses when the monthly free allowance runs out rather than billing. Auto-pauses after an hour idle; waking takes up to a minute. One free database offer per subscription. | Free |
| Two managed identities | The app's (signs in to SQL and Key Vault, no passwords) and the deploy identity (GitHub Actions signs in with OpenID Connect, no stored secrets). | Free |
| Key Vault | Holds the first-run setup code. | Pennies |
| Log Analytics + Application Insights | Logs, errors and performance. Ingestion is capped to stay in the free allowance. | Free |

To keep the app warm instead of scaling to zero (a few dollars a month), deploy with `minReplicas=1` in the template's parameters.

---

## First-time setup

You need an Azure subscription where you're **Owner** (the template grants roles to the two identities). Free Trial is fine.

### 0. Clean up a failed attempt

Skip this if you haven't deployed before.

A failed run leaves resources behind, and their regions can't be changed. In Cloud Shell, delete the old resource group:

```bash
az group delete --name rg-requiem --yes
```

The deleted Key Vault keeps its name reserved for 7 days. List deleted vaults; if this prints a name, purge it with the next command:

```bash
az keyvault list-deleted --query "[].name" -o tsv
```

```bash
az keyvault purge --name <name it printed>
```

### 1. Create the resources (Azure Cloud Shell)

Open **https://shell.azure.com** and choose **Bash**. Cloud Shell already has the Azure CLI and Bicep. Get the code, or update it if you cloned it before:

```bash
git clone https://github.com/jrnicol/requiem.git || git -C requiem pull
```

```bash
cd ~/requiem
```

```bash
az group create --name rg-requiem --location westus3
```

You become the database's administrator, and a random setup code is generated. **Copy the setup code it prints:** you need it in step 6.

```bash
export REQUIEM_SQL_ADMIN_OBJECT_ID=$(az ad signed-in-user show --query id -o tsv)
export REQUIEM_SQL_ADMIN_LOGIN=$(az ad signed-in-user show --query userPrincipalName -o tsv)
export REQUIEM_SETUP_CODE=$(openssl rand -base64 18)
echo "Setup code: $REQUIEM_SETUP_CODE"
```

```bash
az deployment group create --resource-group rg-requiem --template-file infra/main.bicep --parameters infra/main.bicepparam --query properties.outputs
```

Keep the outputs. `appUrl`, `containerAppName`, `resourceGroupName`, `deployClientId`, `tenantId` and `subscriptionId` are used below. If it fails, see **If something fails** below.

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
| `AZURE_RESOURCE_GROUP` | `resourceGroupName` |
| `AZURE_CONTAINERAPP_NAME` | `containerAppName` |

### 4. First run of the workflow

Open **Actions > Build and deploy > Run workflow**. The **build** job tests the app and publishes the container image to `ghcr.io/jrnicol/requiem`.

The **deploy** job may fail the first time if GitHub made the new package private, because then Azure can't pull the image. On this repository the package took the public repo's visibility automatically (2026-10-24), and deploy worked first time. If it fails with a pull or authorization error, do step 5.

### 5. If needed: make the image public, then deploy

1. On GitHub, open your profile's **Packages** tab, then **requiem > Package settings**.
2. Under **Danger Zone**, choose **Change visibility > Public**. The image only holds what's already in this public repository.
3. Back in **Actions**, open that workflow run and choose **Re-run failed jobs**.

The deploy job points the container app at the new image. On start, the app migrates the database and seeds the catalog.

### 6. Create the first GM

Open the `appUrl` from step 1. The first load can take a minute while the app and database wake up. You'll be sent to `/setup`. Enter the **setup code** from step 1 and create your GM account.

Setup locks itself as soon as an account exists, so the code is useless from then on. Deleting it is tidying, not security.

Subscription Owner can manage the vault but not its secrets, so give yourself the secrets role once in Cloud Shell:

```bash
az role assignment create --role "Key Vault Secrets Officer" --assignee $(az ad signed-in-user show --query id -o tsv) --scope $(az keyvault show --name <keyVaultName from step 1> --query id -o tsv)
```

After a minute or two, delete the code:

```bash
az keyvault secret delete --vault-name <keyVaultName from step 1> --name Auth--SetupCode
```

---

## If something fails

| Error | Meaning | Fix |
| --- | --- | --- |
| `RegionDoesNotAllowProvisioning` on the SQL server | That region isn't accepting new SQL servers for your subscription. | Pick another region for the database, e.g. `export REQUIEM_SQL_LOCATION=centralus`, then redo step 0 and step 1. |
| A message about the SQL **free offer** | The subscription has already used its one free database. | Ask, and the template can switch to a small paid database. |
| Deploy job: image pull **unauthorized** / revision failed | The `ghcr.io` package is still private. | Step 5. |
| Deploy job: `AADSTS700213: No matching federated identity record found` | Azure doesn't trust the subject in GitHub's sign-in token. GitHub now writes numeric IDs into it (`repo:jrnicol@9485852/requiem@1431808700:environment:production`). | The template uses that form. If the log's "subject claim" line shows something different, set `githubRepository` in `infra/main.bicepparam` to match and redeploy, or run `az identity federated-credential update --name github-production --identity-name id-requiem-deploy --resource-group rg-requiem --issuer https://token.actions.githubusercontent.com --subject "<subject claim>" --audiences api://AzureADTokenExchange`. |
| The site shows a .NET sample page | The workflow hasn't deployed yet, or the template was rerun (which resets the image). | Run the workflow. |
| First sign-in fails after a quiet spell | The database was paused and is waking up. | Wait a minute and try again. |

## Day to day

- **Deploying:** push to `main`, or run the workflow by hand.
- **Changing infrastructure:** edit `infra/main.bicep`, then rerun the `az deployment group create` command from step 1 in Cloud Shell. Set the three `export` lines again first. Rerunning resets the container image to a placeholder, so run the workflow afterwards.
- **Watching it:** open the container app's **Log stream** for live logs, and **Application Insights > Failures** for errors.
