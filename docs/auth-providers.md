# Setting up the provider legs: Entra ID and Google

The `auth (providers)` job in `.github/workflows/ci.yml` runs one end-to-end
test against Entra ID and one against Google (ADR 0100, point 4 as superseded
by [ADR 0223](adr/0223-provider-legs-by-federated-credentials.md)). It runs
on pushes to `main`, in the `auth-providers` environment, which only `main`
deploys to. It is not a required check.

**No credential is stored anywhere.** The job's own GitHub OIDC token is the
only credential, and each provider is set up to trust it:

- **Entra ID** trusts it as a *federated identity credential* on an app
  registration. The test sends it as the client assertion of a
  client-credentials grant.
- **Google** trusts it through a *workload identity pool*. The test exchanges
  it at Google's STS, then asks IAM Credentials for a service account's ID
  token.

What goes into the environment is identifiers only. Until they're there,
both legs skip. This page is the setup, for the maintainer to do once per
tenant. Each leg is independent: set up one and the other keeps skipping.

The token GitHub issues for the job has these claims, which everything below
matches:

| Claim | Value |
|---|---|
| `iss` | `https://token.actions.githubusercontent.com` |
| `sub` | `repo:Hafeok/Varve:environment:auth-providers` |
| `repository` | `Hafeok/Varve` |
| `environment` | `auth-providers` |

The subject is case-sensitive in both providers. It has to read
`Hafeok/Varve` exactly as GitHub spells the repository.

## Entra ID

You need a test tenant, and in it the right to create app registrations
(Application Administrator, or Cloud Application Administrator) and to grant
admin consent.

`deploy/azure/entra/main.bicep` already creates *Varve's API* (`api://varve`,
v2 tokens, the app roles) and a *calling application* that holds `Varve.Write`
as an application permission. You can reuse both: give the calling
application the federated credential of step 3 too, and skip steps 1, 2 and
4. Otherwise, by hand:

1. **The API's registration.** *Entra ID → App registrations → New
   registration*, single tenant, named for example `varve-api-test`.
   - *Expose an API*: set the Application ID URI, for example `api://varve`.
     This is the **audience**.
   - *App roles → Create app role*: display name and value `Varve.Write`,
     allowed member types *Applications*.
   - *Manifest*: set `"requestedAccessTokenVersion": 2` (shown as
     `accessTokenAcceptedVersion` in the older manifest), so the tokens are v2.
     The test reads `oid` as the subject and expects the v2 issuer
     `https://login.microsoftonline.com/{tenant}/v2.0`.
2. **The client's registration.** *New registration* again, single tenant,
   named for example `varve-auth-providers`. **Add no client secret and no
   certificate.**
   - *API permissions → Add a permission → My APIs →* the API above →
     *Application permissions → `Varve.Write`*, then *Grant admin consent*.
     The role then arrives in the token's `roles` claim.
3. **The federated credential**, on the client's registration: *Certificates
   & secrets → Federated credentials → Add credential*, scenario *GitHub
   Actions deploying Azure resources*, or *Other issuer* with the same values:

   | Field | Value |
   |---|---|
   | Issuer | `https://token.actions.githubusercontent.com` |
   | Organization / repository | `Hafeok` / `Varve` |
   | Entity type | *Environment*, `auth-providers` |
   | Subject (as Entra shows it) | `repo:Hafeok/Varve:environment:auth-providers` |
   | Audience | `api://AzureADTokenExchange` |
   | Name | `github-auth-providers` |

   With the Azure CLI:

   ```bash
   az ad app federated-credential create --id <client-app-id> --parameters '{
     "name": "github-auth-providers",
     "issuer": "https://token.actions.githubusercontent.com",
     "subject": "repo:Hafeok/Varve:environment:auth-providers",
     "audiences": ["api://AzureADTokenExchange"]
   }'
   ```
4. **A role other than `Varve.Write`?** Add `VARVE_ENTRA_ROLE` with its value
   to the `env:` of the job's test step in `ci.yml`. Without it, the test
   grants `Varve.Write`.

**The values for the environment** (*Settings → Environments →
`auth-providers` → Environment secrets*):

| Name | Value |
|---|---|
| `VARVE_ENTRA_TENANT` | the tenant's id (a GUID) |
| `VARVE_ENTRA_CLIENT_ID` | the client registration's *Application (client) ID* |
| `VARVE_ENTRA_AUDIENCE` | the API's Application ID URI, for example `api://varve` |

These are identifiers, not credentials. They're secrets only because
repo-standard can't yet declare an environment's variables (#239). Once it
can, they move to variables.

## Google

You need a Google Cloud project for the test, with the IAM, IAM Credentials
and Security Token Service APIs enabled, and the right to create workload
identity pools and service accounts in it (Workload Identity Pool Admin,
Service Account Admin, and Project IAM Admin or the project's owner).

```bash
PROJECT_ID=<project-id>
PROJECT_NUMBER=$(gcloud projects describe "$PROJECT_ID" --format='value(projectNumber)')
gcloud services enable iam.googleapis.com iamcredentials.googleapis.com sts.googleapis.com --project "$PROJECT_ID"
```

1. **The workload identity pool**:

   ```bash
   gcloud iam workload-identity-pools create github \
     --project "$PROJECT_ID" --location global --display-name "GitHub Actions"
   ```
2. **Its OIDC provider**, with the attribute mapping and the condition. The
   condition is what admits only this repository's `auth-providers`
   environment. Without it, any repository on GitHub could present a token.

   ```bash
   gcloud iam workload-identity-pools providers create-oidc varve \
     --project "$PROJECT_ID" --location global --workload-identity-pool github \
     --issuer-uri "https://token.actions.githubusercontent.com" \
     --attribute-mapping "google.subject=assertion.sub,attribute.repository=assertion.repository,attribute.environment=assertion.environment" \
     --attribute-condition "assertion.repository == 'Hafeok/Varve' && assertion.environment == 'auth-providers'"
   ```

   Leave the allowed audiences at the default. The default is the provider's
   own URL, `https://iam.googleapis.com/projects/$PROJECT_NUMBER/locations/global/workloadIdentityPools/github/providers/varve`,
   and that's the audience the test asks GitHub for.
3. **The service account.** It needs no role on the project and **no key**:

   ```bash
   gcloud iam service-accounts create varve-auth-providers \
     --project "$PROJECT_ID" --display-name "Varve auth (providers)"
   ```
4. **The binding.** The pool's principals for this repository may mint the
   service account's ID tokens, and nothing else:

   ```bash
   gcloud iam service-accounts add-iam-policy-binding \
     "varve-auth-providers@$PROJECT_ID.iam.gserviceaccount.com" \
     --project "$PROJECT_ID" \
     --role roles/iam.serviceAccountOpenIdTokenCreator \
     --member "principalSet://iam.googleapis.com/projects/$PROJECT_NUMBER/locations/global/workloadIdentityPools/github/attribute.repository/Hafeok/Varve"
   ```

   The provider's condition already narrows the pool to the `auth-providers`
   environment. To narrow the binding to it as well, bind the one subject
   instead:
   `principal://iam.googleapis.com/projects/$PROJECT_NUMBER/locations/global/workloadIdentityPools/github/subject/repo:Hafeok/Varve:environment:auth-providers`.

**The values for the environment** (as environment secrets, for the reason
given under Entra):

| Name | Value |
|---|---|
| `VARVE_GOOGLE_WORKLOAD_IDENTITY_PROVIDER` | `projects/<project-number>/locations/global/workloadIdentityPools/github/providers/varve`, the provider's resource name with no scheme and no leading `//` |
| `VARVE_GOOGLE_SERVICE_ACCOUNT_EMAIL` | `varve-auth-providers@<project-id>.iam.gserviceaccount.com` |

The audience of the ID token is fixed in the test as
`https://w3id.org/varve/tests`, and the server under test is configured with
it. Nothing needs setting for it.

## Seeing the legs run

The job runs on every push to `main`. To run it without a change, re-run the
`auth (providers)` job of the latest CI run on `main` from the Actions tab.

**Before any setup**, both tests skip:

```
skipped Varve.Server.Tests.ProviderTests.Entra_federated_client_credentials_token_writes
  The entra leg needs VARVE_ENTRA_TENANT, VARVE_ENTRA_CLIENT_ID, VARVE_ENTRA_AUDIENCE (ADR 0100, ADR 0223; docs/auth-providers.md).
skipped Varve.Server.Tests.ProviderTests.Google_workload_identity_id_token_writes
  The google leg needs VARVE_GOOGLE_WORKLOAD_IDENTITY_PROVIDER, VARVE_GOOGLE_SERVICE_ACCOUNT_EMAIL (ADR 0100, ADR 0223; docs/auth-providers.md).
```

The four container-leg tests in the same class skip as well. Their own jobs
run them.

**A correct first run** of a configured leg reports that test as passed, with
the other leg still skipped if it isn't set up yet. The test has then shown
that:

- GitHub issued the token;
- the provider exchanged it: Entra's token endpoint, or Google's STS and then
  `generateIdToken`;
- the server, configured with the provider's discovery document and keys over
  HTTPS, accepted the resulting token and let it write, by `roles` for Entra
  and by `email` for Google.

**A configured leg never skips.** Once any of its identifiers is set, these
all fail the job:

- a missing identifier, for example "The entra leg is configured
  (VARVE_ENTRA_TENANT) and is missing VARVE_ENTRA_CLIENT_ID, …";
- a missing OIDC token;
- a refused exchange. The provider's answer is in the message:
  - Entra's `AADSTS70021` is a federated credential that doesn't match the
    subject or audience;
  - Google's `unauthorized_client` from STS is a token the attribute
    condition rejected;
  - a `403` from IAM Credentials is a missing binding.

Once both legs pass, set `VARVE_AUTH_LEGS_REQUIRED: entra,google` on the
job's test step (ADR 0100 point 5). An identifier removed later then fails the
job instead of quietly skipping it.

**repo-standard** reports the five names as "declared and not present" until
they're added. That is expected, and it is setup, not drift. Once they're
added, the weekly check is clean.
