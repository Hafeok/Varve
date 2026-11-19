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

<!-- ci: none — a sketch, not the shipped files: kubernetes.yml kind deploys deploy/kubernetes/, which is this sketch made whole -->
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
kubeconform, strict, against Kubernetes 1.34, on every pull request, and
deploys them in kind every night and on every change to them
([What CI deploys](#what-ci-deploys-in-kind)).

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
the client ids of the store's and the operator's identities. Leave `{pod}`
in `handover.yaml`'s `--address` as it is: the controller fills it in with
each pod's name at run time. Then apply both:

<!-- ci: kubernetes.yml kind — through .github/kind/'s patches, which change only what kind cannot give (below) -->
```sh
kubectl apply --filename deploy/kubernetes/fleet.yaml --filename deploy/kubernetes/handover.yaml
kubectl --namespace varve rollout status statefulset/varve
kubectl --namespace varve rollout status deployment/varve-handover
```

Neither file sets `Varve:ObjectStorage:ReaderId`, and the cache is an
`emptyDir`, so a pod that restarts uncleanly announces a new reader and the
old record holds extent sets back until `varve storage readers forget`
removes it (ADR 0160, #303).

## What CI deploys in kind

`kubernetes.yml`'s `kind` job deploys both files every night, and on every
change to `deploy/kubernetes/`, to a one-node kind cluster. It builds the
image from the commit and applies the files through the patches in
`.github/kind/`, which change only what kind cannot give:

- **The store is Azurite**, in the cluster, not Azure Blob Storage. Each pod
  reaches it through a forwarder on its own loopback address, because the
  server signs with Azurite's development key only there (ADR 0137).
- **The fleet is anonymous**, and the controller runs without its sign-in:
  no identity provider is reachable from the cluster.
- **Each pod requests a quarter of a CPU and 512 MiB**, so that three fit on
  one runner.

**What it checks**, through each pod's own address:
1. The three pods and the controller turn ready. `varve-0` writes the system
   dataset, and the others follow it.
2. A dataset declared through `varve-2` is placed on `varve-0`, with
   `varve-1` and `varve-2` its warm standbys.
3. A write through `varve-2` is forwarded to `varve-0`, and `varve-1` reads
   it.
4. **A hung writer.** `varve-0`'s process is stopped, so it releases nothing.
   Its readiness fails, and the controller sees the pod turn not ready and
   takes over: `varve-1` holds the dataset and the system dataset, by the
   trigger `probe`. A write through `varve-2` then reaches `varve-1`.
5. **A deleted writer.** `varve-1`'s pod is deleted, releases in its grace,
   and another member holds the dataset; writes and reads go on.

## Not run in CI

What on this page no CI job runs, and why (ADR 0248):

- **A sketch.** It is not the shipped files. `kubernetes.yml` `kind` deploys
  `deploy/kubernetes/`, which is this sketch made whole.
- **Workload identity.** kind has none, so neither the store's identity nor
  the controller's federated token is exercised. Neither is Entra: the fleet
  in kind is anonymous.
- **The sample's resource requests**, one CPU and 2 GiB per pod, which one
  runner cannot fit three times.
- **A node's loss and a rolling upgrade.** The cluster has one node, and the
  job deploys one image.
