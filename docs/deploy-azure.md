# Deploying to Azure

How dashboard-starter goes live: one Azure App Service runs the API and serves the React app, one Azure SQL database holds the data, and GitHub Actions deploys every merge to `main`.

```
GitHub: merge to main
  -> CI (ci.yml): build, lint, schema checks against a real SQL Server
  -> Deploy (deploy.yml): build, migrate, app login, configure, deploy, health check
       -> Azure App Service (Linux, .NET 10): API + React build in wwwroot
       -> Azure SQL Database
```

Setup is a handful of commands you run once. After that, deploying is merging a pull request.

## What you need

- An Azure subscription. A Visual Studio (MSDN) subscription's monthly credit covers this comfortably.
- The [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli), signed in with `az login`.
- Admin rights on your GitHub repo (yours, or your fork).

The commands below are bash. On Windows, run them in Git Bash or WSL, or adjust the variable syntax for PowerShell.

## 1. Pick names

```bash
RG=dashboard-starter-demo          # resource group
APP=dashboard-starter-demo         # web app name: <APP>.azurewebsites.net, unique across Azure
LOCATION=westus3                   # any region; westus3 is Phoenix
REPO=mvmorten/dashboard-starter    # your GitHub owner/repo
```

The SQL server is named `<APP>-sql` and the App Service plan `<APP>-plan`.

## 2. Create the Azure resources

Make two passwords first: one for the SQL admin login (schema changes only) and one for the API's own login (data only). Avoid `;`, `'`, and `"`, which would need escaping inside a connection string. A long random value is easiest:

```bash
SQL_ADMIN_PASSWORD="$(openssl rand -base64 24)Aa1!"
APP_DB_PASSWORD="$(openssl rand -base64 24)Aa1!"
```

(The `Aa1!` suffix guarantees SQL Server's complexity rules are met.) Keep both; they go into GitHub in step 4.

Then create the group and run the Bicep file:

```bash
az group create --name $RG --location $LOCATION

az deployment group create --resource-group $RG --template-file infra/main.bicep \
  --parameters appName=$APP sqlAdminPassword="$SQL_ADMIN_PASSWORD"
```

[`infra/main.bicep`](../infra/main.bicep) creates the App Service plan (B1, Linux), the web app (.NET 10, HTTPS only), the SQL server, its firewall rule for Azure services, and the database. It doesn't create the schema or hold any connection string; the deploy workflow does both. Run it again whenever the file changes. It only changes what's different.

**Basic or free database?** By default the database is Basic: always on, a few dollars a month, and every request answers straight away. To pay nothing, add `useFreeDatabase=true` to the parameters for Azure SQL's [free offer](https://learn.microsoft.com/azure/azure-sql/database/free-offer) instead: serverless, one per subscription, with a monthly allowance of compute and 32 GB of storage. The catch is that it pauses after an hour without connections, and the first request after a pause waits while it wakes up (up to about a minute). If the allowance runs out, it pauses until next month instead of billing. Fine for a personal sandbox, not great for a public demo. Decide before the first run if you can; switching later works, but it's a change to a live database.

**Check the runtime.** The template asks for `DOTNETCORE|10.0`. If the deployment complains about it, see what your region offers with `az webapp list-runtimes --os linux | grep DOTNET`.

## 3. Let GitHub deploy without a password

GitHub Actions signs in to Azure with OpenID Connect: GitHub vouches for "a deploy job in this repo, in the `demo` environment", and Azure is told to trust exactly that. No Azure secret is stored in GitHub.

```bash
SUBSCRIPTION_ID=$(az account show --query id --output tsv)
TENANT_ID=$(az account show --query tenantId --output tsv)

# An identity for the deploy workflow.
CLIENT_ID=$(az ad app create --display-name "$APP-deploy" --query appId --output tsv)
az ad sp create --id $CLIENT_ID

# Trust GitHub tokens from this repo's "demo" environment, and nothing else.
az ad app federated-credential create --id $CLIENT_ID --parameters "{
  \"name\": \"github-demo\",
  \"issuer\": \"https://token.actions.githubusercontent.com\",
  \"subject\": \"repo:$REPO:environment:demo\",
  \"audiences\": [\"api://AzureADTokenExchange\"]
}"

# Allow it to manage this resource group only.
az role assignment create --assignee $CLIENT_ID --role Contributor \
  --scope /subscriptions/$SUBSCRIPTION_ID/resourceGroups/$RG
```

Contributor on one resource group is enough to add and remove the temporary firewall rule, set app settings, and deploy code. It can't touch anything else in the subscription.

## 4. Configure GitHub

In the repo, **Settings > Environments > New environment**, name it `demo`. Under **Deployment branches and tags**, choose **Selected branches** and add `main`, so nothing but `main` can ever deploy.

Add two **environment secrets** to `demo`:

| Secret | Value |
| --- | --- |
| `SQL_ADMIN_PASSWORD` | `$SQL_ADMIN_PASSWORD` from step 2 |
| `APP_DB_PASSWORD` | `$APP_DB_PASSWORD` from step 2 |

Then **Settings > Secrets and variables > Actions > Variables**, add these **repository variables** (repository, not environment: CI reads `AZURE_WEBAPP_NAME` to decide whether to deploy at all):

| Variable | Value |
| --- | --- |
| `AZURE_CLIENT_ID` | `$CLIENT_ID` |
| `AZURE_TENANT_ID` | `$TENANT_ID` |
| `AZURE_SUBSCRIPTION_ID` | `$SUBSCRIPTION_ID` |
| `AZURE_RESOURCE_GROUP` | `$RG` |
| `AZURE_WEBAPP_NAME` | `$APP` |
| `CANONICAL_HOST` | Leave unset for now (see step 6) |

None of the variables are secret: they say where to deploy, not how to get in. With the GitHub CLI it's quicker:

```bash
gh secret set SQL_ADMIN_PASSWORD --env demo --body "$SQL_ADMIN_PASSWORD"
gh secret set APP_DB_PASSWORD --env demo --body "$APP_DB_PASSWORD"
gh variable set AZURE_CLIENT_ID --body "$CLIENT_ID"
gh variable set AZURE_TENANT_ID --body "$TENANT_ID"
gh variable set AZURE_SUBSCRIPTION_ID --body "$SUBSCRIPTION_ID"
gh variable set AZURE_RESOURCE_GROUP --body "$RG"
gh variable set AZURE_WEBAPP_NAME --body "$APP"
```

(Create the `demo` environment and its branch rule in the web UI first.)

## 5. Deploy

Merge anything to `main`, or open **Actions > Deploy > Run workflow** to deploy the current `main` without a new commit. The deploy job:

1. Builds the React app and publishes the API with the build in `wwwroot`.
2. Signs in to Azure (step 3) and opens the SQL firewall to the runner's IP address for the length of the job.
3. Runs `src/Db migrate` with the admin login, retrying while a paused database wakes up.
4. Runs `src/Db app-user`, which creates the API's login (or resets its password) with data access only.
5. Closes the firewall again, even if a step failed.
6. Sets the app's connection string (the API's login, not the admin) and `CanonicalHost`.
7. Deploys the zip and waits for `/api/health` to report healthy.

Then open `https://<APP>.azurewebsites.net`.

Forks: CI skips the deploy job unless `AZURE_WEBAPP_NAME` is set, so a fork stays green without any Azure setup.

## 6. Custom domain (optional)

To serve the app on your own domain, for example `dashboard.example.com`, with a second name that redirects to it:

1. At your DNS provider, for each host name add:
   - a `CNAME` record from the name (`dashboard`) to `<APP>.azurewebsites.net`, and
   - a `TXT` record `asuid.dashboard` holding the value of
     `az webapp show --resource-group $RG --name $APP --query customDomainVerificationId --output tsv`.
2. Add each host name and a free App Service managed certificate:

   ```bash
   HOST=dashboard.example.com
   az webapp config hostname add --resource-group $RG --webapp-name $APP --hostname $HOST
   THUMBPRINT=$(az webapp config ssl create --resource-group $RG --name $APP --hostname $HOST \
     --query thumbprint --output tsv)
   az webapp config ssl bind --resource-group $RG --name $APP \
     --certificate-thumbprint $THUMBPRINT --ssl-type SNI
   ```

3. Set the repository variable `CANONICAL_HOST` to the main name (`dashboard.example.com`) and run the Deploy workflow.

With `CanonicalHost` set, the API permanently redirects every other host name, including `<APP>.azurewebsites.net`, to the canonical one. That matters from step 4 (authentication) on: cookies belong to one host, so a user who signed in on one name would be signed out on another. `/api/health` is answered on every host, so health checks keep working on the default name.

The certificates renew automatically. Host names and certificates aren't in the Bicep file, so rerunning it leaves them alone.

## Day to day

- **Rotate the API's password:** change the `APP_DB_PASSWORD` secret and run Deploy. `app-user` resets the login's password and the app gets the new connection string in the same run.
- **Rotate the admin password:** `az sql server update --resource-group $RG --name $APP-sql --admin-password "<new>"`, then update `SQL_ADMIN_PASSWORD`.
- **Logs:** `az webapp log tail --resource-group $RG --name $APP`, or **Log stream** in the portal.
- **Connect from SSMS:** add your IP under the SQL server's **Networking** settings, connect to `<APP>-sql.database.windows.net` with the admin login, and remove the rule when done. If you chose the free database, an open SSMS connection keeps it awake and uses up its monthly allowance.
- **Tear it all down:** `az group delete --name $RG`. Then delete the `$APP-deploy` app registration (`az ad app delete --id $CLIENT_ID`).
