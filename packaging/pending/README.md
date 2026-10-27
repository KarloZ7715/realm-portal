# Pending workflow change for #131

The box GitHub PAT lacks the `workflow` scope, so `.github/workflows/release-linux.yml`
cannot be pushed from this environment.

`release-linux-publish-job.patch` is the full diff against `origin/main` for the tag-only
**publish** job (draft → upload → verify → publish; `--prerelease --latest=false` for rc).

Apply on this branch once a token with Workflows read/write is available:

```bash
git apply packaging/pending/release-linux-publish-job.patch
# or: patch -p1 < packaging/pending/release-linux-publish-job.patch
rm -rf packaging/pending
git add .github/workflows/release-linux.yml
git commit -m "ci(release): publish GitHub releases (prerelease for rc tags)"
git push
```

Then delete this `packaging/pending/` directory in that same commit.
