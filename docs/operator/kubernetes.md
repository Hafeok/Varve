# Run on Kubernetes

A fleet on Kubernetes is one StatefulSet of identical pods sharing one object
store (ADRs 0163, 0168). The first pod writes the system dataset; every pod
follows the others' datasets and forwards writes. [The fleet](fleet.md) has
the model.

## The shape

- **A StatefulSet** gives each pod a stable name, `varve-0`, `varve-1`, …,
  which is its instance: `Varve:ObjectStorage:Instance` from the pod's name.
- **`Varve:Fleet:Writer: varve-0`** in every pod's configuration: the pod of
  that name writes the system dataset, and the others follow it. Every pod
  may claim the datasets placed on it.
- **A headless service** gives each pod its own address, which is its
  `Varve:Fleet:InternalUrl`, recorded in the writer record and used by the
  others to forward writes:
  `http://varve-0.varve-headless.varve.svc.cluster.local:8080`. The address
  carries callers' tokens: keep it inside the cluster network, or serve it
  over TLS.
- **A second service** load-balances clients across every pod.
- **Workload identity** reaches the store: Azure workload identity
  (`AZURE_FEDERATED_TOKEN_FILE`, `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`), or
  `AWS_WEB_IDENTITY_TOKEN_FILE` and `AWS_ROLE_ARN`. No key (ADR 0137).
- **Readiness** is `/health/ready`, which includes the management plane;
  liveness is `/health/live`.

## A sketch

```yaml
apiVersion: v1
kind: Service
metadata: { name: varve-headless, namespace: varve }
spec:
  clusterIP: None
  selector: { app: varve }
  ports: [{ name: http, port: 8080 }]
---
apiVersion: v1
kind: Service
metadata: { name: varve, namespace: varve }
spec:
  selector: { app: varve }
  ports: [{ name: http, port: 80, targetPort: 8080 }]
---
apiVersion: apps/v1
kind: StatefulSet
metadata: { name: varve, namespace: varve }
spec:
  serviceName: varve-headless
  replicas: 3
  selector: { matchLabels: { app: varve } }
  template:
    metadata:
      labels: { app: varve, azure.workload.identity/use: "true" }
    spec:
      serviceAccountName: varve       # federated with the store's identity
      containers:
        - name: varve
          image: ghcr.io/hafeok/varve@sha256:{digest}
          args: [serve]
          ports: [{ containerPort: 8080 }]
          env:
            - { name: POD_NAME, valueFrom: { fieldRef: { fieldPath: metadata.name } } }
            - { name: VARVE__OBJECTSTORAGE__INSTANCE, value: "$(POD_NAME)" }
            - { name: VARVE__FLEET__WRITER, value: varve-0 }
            - { name: VARVE__FLEET__INTERNALURL, value: "http://$(POD_NAME).varve-headless.varve.svc.cluster.local:8080" }
            - { name: VARVE__OBJECTSTORAGE__PROVIDER, value: Azure }
            - { name: VARVE__OBJECTSTORAGE__CONTAINERURL, value: "https://{account}.blob.core.windows.net/datasets" }
            - { name: VARVE__OBJECTSTORAGE__CACHEPATH, value: /var/cache/varve }
            - { name: VARVE__AUTH__MODE, value: Oidc }
            - { name: VARVE__AUTH__AUTHORITY, value: "https://login.microsoftonline.com/{tenant}/v2.0" }
            - { name: VARVE__AUTH__AUDIENCES__0, value: "api://varve" }
            - { name: VARVE__MANAGE__BOOTSTRAPOPERATORS__0, value: Varve.Operator }
          readinessProbe: { httpGet: { path: /health/ready, port: 8080 } }
          livenessProbe: { httpGet: { path: /health/live, port: 8080 } }
          volumeMounts: [{ name: cache, mountPath: /var/cache/varve }]
      volumes: [{ name: cache, emptyDir: {} }]
```

The cache is an `emptyDir`: everything in it is rebuilt from the store.

## Datasets

Declare datasets once the fleet is up, with `varve apply` against the
`varve` service ([Datasets](datasets.md)). A dataset placed on no pod is
written by `varve-0`; `varve:placedOn "varve-2"` spreads the writing, and
changing it moves the writer with no restart ([The fleet](fleet.md#who-writes-a-dataset-placement)).

When `varve-0` restarts, management writes are unavailable until it is back;
reads, and data writes to the datasets other pods write, go on.
