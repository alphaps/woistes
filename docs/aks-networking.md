# AKS Networking & Public Access

How Woistes is exposed publicly on AKS, and two non-obvious gotchas that broke
access during initial setup. Both are consequences of serving over **plain HTTP**
(no TLS cert yet) and using a **separately-installed NGINX ingress controller**.

## Topology

```
[Browser] --> woistes.westeurope.cloudapp.azure.com (20.23.123.85)
           --> Azure LoadBalancer (Service: ingress-nginx-controller)
           --> NGINX Ingress Controller pod
           --> Service: woistes (ClusterIP) --> app pod :8080
```

- The public DNS name is an Azure-assigned label on the ingress controller's
  public IP. Set with:
  ```
  az network public-ip update \
    --name <kubernetes-...> -g MC_woistes_group_woistes_francecentral \
    --dns-name woistes
  ```
- The NGINX ingress controller was installed separately (Helm), **not** part of
  the Woistes chart. Anything configured on its Service via annotations must be
  re-applied if the controller is reinstalled.

## Gotcha 1 — Azure LB health probe must target `/healthz`

**Symptom:** connections to the public IP time out (HTTP 000), even though the
pod, Service, ingress, and NSG are all healthy.

**Cause:** the Azure Load Balancer's default health probe is an **HTTP probe on
path `/`**. NGINX forwards `/` to the app, which returns **302** (redirect to
Google login). Azure HTTP probes treat anything other than **200** as
unhealthy, so the LB marks the backend down and silently drops all inbound
traffic.

**Fix:** point the probe at NGINX's own built-in `/healthz` endpoint (returns
200, served by the controller itself — it comes for free with the ingress
controller, independent of app routes):

```
kubectl annotate svc ingress-nginx-controller \
  service.beta.kubernetes.io/azure-load-balancer-health-probe-request-path=/healthz \
  --overwrite
```

Azure reconciles the probe within ~30s. Verify:

```
az network lb probe list -g MC_woistes_group_woistes_francecentral \
  --lb-name kubernetes -o table   # requestPath should read /healthz
```

> Git Bash note: prefix the `kubectl annotate` with `MSYS_NO_PATHCONV=1`,
> otherwise MSYS rewrites `/healthz` into a Windows path like
> `C:/Program Files/Git/healthz`.

This annotation lives on the ingress-nginx Service. **Re-apply it if you
reinstall the ingress controller** (or bake it into the controller's Helm
values under `controller.service.annotations`).

## Gotcha 2 — OAuth "Correlation failed" over HTTP

**Symptom:** Google login succeeds, but the callback to `/signin-google`
returns **HTTP 500** with `AuthenticationFailureException: Correlation failed.`

**Cause:** before redirecting to Google, ASP.NET sets a temporary
`.AspNetCore.Correlation.*` cookie (CSRF protection) that must come back on the
callback. Its defaults are `SameSite=None` + `Secure`, which browsers **refuse
to store over plain HTTP** — so it's missing on return and the CSRF check fails.

**Fix (HTTP-only workaround, in `Program.cs`):**

```csharp
options.CorrelationCookie.SameSite = SameSiteMode.Lax;            // sent on top-level GET redirect back
options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;  // allow over HTTP
```

`Lax` is sufficient because the return from Google is a top-level GET
navigation.

> **Revert this once TLS is in place.** With HTTPS, restore the secure defaults
> (`SameSite=None`, `SecurePolicy=Always`). Proper fix = add cert-manager +
> Let's Encrypt and serve the site over HTTPS.

## Google Cloud Console settings

- **Authorized redirect URI:** `http://woistes.westeurope.cloudapp.azure.com/signin-google`
  (Google rejects bare IPs — that's why the DNS label is required.)
- Client ID / Secret are injected via the `woistes-secrets` Kubernetes Secret,
  populated from GitHub Actions secrets `GOOGLE_CLIENT_ID` / `GOOGLE_CLIENT_SECRET`
  passed as Helm overrides at deploy time.
