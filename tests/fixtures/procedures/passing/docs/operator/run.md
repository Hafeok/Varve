# Run

Run by a job of a workflow, and by a job of eng/ci.cs, with a note:

<!-- ci: ci.yml build; eng/ci.cs test-server — the server tests -->
```sh
varve serve
```

1. Inside a list:

   <!-- ci: ci.yml build -->
   ```sh
   varve query
   ```

An output, which is no procedure:

<!-- ci: example — what the command prints -->
```
lock: held
```

<!-- ci: none — it needs a systemd host, and no job starts a unit -->
```ini
[Service]
```

## Not run in CI

- **Run.** The systemd unit: it needs a systemd host.
