# Python Tooling

## Ruff

- Never allow Ruff to create a repository-local cache.
- Every Ruff invocation must include `--no-cache`.

```powershell
uvx ruff check --no-cache <files>
uvx ruff format --no-cache --check <files>
```
