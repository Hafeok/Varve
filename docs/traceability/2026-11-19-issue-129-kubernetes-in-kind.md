# deploy/kubernetes/ deployed in kind, nightly and on every change to it

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The transcript itself is
> held by the maintainer.

| | |
|---|---|
| **Issues** | [#129](https://github.com/Hafeok/Varve/issues/129) (the operator guide, pull request D of [the split](https://github.com/Hafeok/Varve/issues/129#issuecomment-6978780204)) |
| **Date** | 2026-11-19 |
| **Tool** | Claude Code, a cloud session (not the maintainer's delegate, ADR 0239); the work was done by a worker the session forked |
| **Model** | `claude-opus-5-5`, from the session's own metadata |
| **Session identifier** | `session_01NbWSYS9TK9P9VYitD1bP8Z` |
| **Branch** | `claude/operator-guide-d-129`, stacked on #345's branch `claude/relaxed-wright-nv07v6` at 18a0d5ab, whose procedure markers and gate it needs |
| **ADRs** | none |

## The prompt

The maintainer's instruction to the session, verbatim:

> For the operator guide session. 1. Branches: push each remaining part to its own branch, one pull request each: claude/operator-guide-b1-129, claude/operator-guide-b2-129, claude/operator-guide-c-129, claude/operator-guide-d-129. Base each on main. If a part depends on #345's changes, stack it on #345's branch and say so in its description. Try each push once; if one is refused, say so on #129 with the exact refusal and do not try another route. […] 4. Each new pull request: Completes its own issue or Completes: none with Refs #129, its decisions filed unaccepted, and its ADR status line in the 'Accepted — filed unaccepted by …' form.

Answer 8 of the maintainer's answers on #129 (2026-11-16) sets this part's
scope: a kind job, nightly and on changes under `deploy/kubernetes/`, not on
every pull request, with its image through `.github/ci-images.yaml`.

The session handed part D to a forked worker. The worker's directive, in
substance (the exact text is in the transcript):
- read the CI image list, its gate and mirror, the `kubernetes-sample` job,
  both sample files, `kubernetes.md` and `nightly.yml`;
- list the kind node image, and anything else pulled, in
  `.github/ci-images.yaml`; Actions by commit SHA, versions resolved from
  upstream on the day;
- write a job that creates a kind cluster, loads the run's image, applies
  `fleet.yaml` and `handover.yaml` with the placeholders filled and a store
  in the cluster, waits for the pods, and checks a write, a read on a
  follower, and that losing the writer moves it by `watch-kubernetes`'s
  takeover; check honestly, and say what is not checked;
- mark `kubernetes.md`'s procedures with the job and update its and
  `fleet.md`'s "Not run in CI";
- run the gates locally, write this record, push once, and open the pull
  request.

## The report

- **Versions, resolved from upstream on 2026-11-19:**
  - kind **v0.33.0**, the latest release (`git ls-remote`: tag at
    `407a9675`). Its default node image, in `pkg/apis/config/defaults/image.go`
    at that tag, is `kindest/node:v1.37.0@sha256:a1ed56cf…`.
  - `helm/kind-action` **v1.15.2**, at `ea822c7b`, the release that checks
    kind's download against the release's SHA-256.
- **The node image's upstream.** kind publishes it on Docker Hub only, which
  no row may name as its upstream (ADR 0099, amendment 2026-10-20). The same
  digest is in kind's staging registry, `gcr.io/k8s-staging-kind/node:v1.37.0`,
  where it is built. The new row `kind-node` copies it from there.
- **The store.** The server signs with Azurite's development key only on a
  loopback address (`AzureBlobStore.IsDevelopmentStorage`, ADR 0137), and
  otherwise asks the platform for a token, which kind cannot give. So Azurite
  runs once in the cluster, and each pod has a native sidecar that forwards
  its own `127.0.0.1:10000` to it. The sidecar runs Azurite's own image, for
  its Node.js, so nothing else is pulled. The container is made over a
  `kubectl port-forward`, again on loopback. The bash Shared Key signature
  was checked against Python's HMAC for the same string to sign.
- **Sign-in.** No identity provider is reachable from the cluster, so the
  fleet is anonymous (with `Production` false, which anonymous mode requires)
  and the controller runs without `--authority`, `--client-id`,
  `--client-assertion` and `--scope`. `Takeovers.FromAsync` sends no bearer
  when none is given, as `fleet-services` already runs it.
- **The writer's loss.** A pod deleted gracefully releases in its grace, so
  the release moves the writer and the controller finds nothing to do. To
  exercise the controller, the check also stops the writer's process
  (SIGSTOP, through `crictl` on the node). It then releases nothing, its
  readiness fails, and only the controller's takeover can move it. The check
  requires the trigger `probe`, which `WriterSet.TakeoverAsync` writes, and
  the controller's own log lines. Both losses are checked, in that order.
- **What changes in the sample**, only through the patches in `.github/kind/`:
  - the image;
  - the store;
  - anonymous mode;
  - no Web UI;
  - a quarter of a CPU and 512 MiB requested per pod, so that three fit on one
    runner.

  Everything else is the files as checked in, and each patch's header says
  why.
- **Not checked, and said so on the page:**
  - workload identity, to the store or for the controller;
  - Entra;
  - the sample's resource requests;
  - a node's loss, on a one-node cluster;
  - a rolling upgrade.
- **Run locally:**
  - `procedures`, `procedures-fixtures`, `site-links`, `sdk-pin`,
    `required-checks`, `register`, `ci-images`, `ci-images-fixture` and
    `licence-headers`: all pass;
  - `dco`, `issue-refs` and `model-names` against `origin/main`;
  - the workflow and the manifests parse as YAML;
  - both scripts pass `bash -n`.

  **Not run here:** kind, and the job itself. This container has no Docker
  daemon, and fetching kubectl to render the patches was refused. The job's
  first run is its test.
- **The first run needs the mirror.** `kind-node` is a new row, so the job
  can pull it only after `mirror-ci-images.yml` has copied it on `main`. That
  run comes out public and red until the maintainer sets the package private
  and re-runs it. Until then, this pull request's run of the job fails at
  the cluster's creation.
