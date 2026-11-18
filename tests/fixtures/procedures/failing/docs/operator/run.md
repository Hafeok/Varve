# Run

A block with no marker:

```sh
varve serve
```

A marker naming a job no workflow has:

<!-- ci: ci.yml no-such-job -->
```sh
varve query
```

A marker naming a workflow that does not exist:

<!-- ci: missing.yml build -->
```sh
varve apply
```

`none` with no reason:

<!-- ci: none -->
```sh
varve feed
```

`none` with its reason, on a page with no section listing it:

<!-- ci: none — it needs a systemd host, and no job starts a unit -->
```ini
[Service]
```
