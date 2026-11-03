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

## Handover: the sample in `deploy/kubernetes/`

[`deploy/kubernetes/`](../../deploy/kubernetes/) is the sketch above made
whole, with handover (ADRs 0179–0181). CI validates both files with
kubeconform, strict, against Kubernetes 1.34.

- **`fleet.yaml`.** Every pod is a member of the system dataset's writer
  set, `Varve:Fleet:Writer: varve-0,varve-1,varve-2`, with
  `Varve:Fleet:AutomaticHandover: true`. Declare datasets with `placedOn` and
  `standbyOn` among the pods ([The fleet](fleet.md#standbys-a-writer-set)).
  A rolling restart then moves each writer to its warm standby: the stopping
  pod releases for shutdown, in its termination grace.
- **`handover.yaml`.** A crashed, hung or partitioned writer releases
  nothing, so a controller turns kubelet's verdict into a takeover. It is
  Varve's own image, running `varve manage watch-kubernetes`:
  - **It watches the fleet's pods** through the API server, by their label
    selector, with its service account's token. It keeps no timer of its
    own. When a pod's `Ready` turns from `True` to anything else, after the
    readiness probe's `failureThreshold`, it takes over from that pod over
    the others' addresses. A pod deleted while ready counts too; a pod
    first seen not ready, or one that only turns ready, does not.
  - **The takeover is `varve manage takeover`'s**, in the same process
    ([The fleet](fleet.md#taking-over-from-a-failed-writer)). A late or
    duplicated trigger, or one for a pod whose writer already moved by
    abdication, finds nothing to do.
  - **Its identity** is the `varve-handover` service account. In the
    cluster it may `get`, `list` and `watch` pods in the namespace, and
    nothing else. Its federated token is exchanged at the identity provider
    for the operator role on the management audience.
  - **A watch the API server ends** is opened again from the last version
    seen; one it refuses as too old (`410`) starts from a fresh list. A
    watch that fails is opened again after two seconds.

Replace each `{…}`: the image digest, the storage account, the tenant, and
the client ids of the store's and the operator's identities.
