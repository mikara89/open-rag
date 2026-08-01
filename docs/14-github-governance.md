# 14 — GitHub Governance

## Status

The settings below are **recommended and not enabled as of 2026-08-01**. The branch-protection API returned `Branch not protected`. Apply the recommendations only after a maintainer explicitly approves repository-setting changes and after the required CI check names have appeared in a successful GitHub Actions run.

## Recommended protection for `main`

- Require changes to enter through a pull request.
- With the current single maintainer, do not require self-approval. Require at least one approval once more than one maintainer can review.
- Dismiss stale approvals when new commits are pushed.
- Require all review conversations to be resolved.
- Require branches to be up to date before merging when the repository's merge volume makes that practical.
- Require these CI checks:
  - `Build, test, and format`
  - `Documentation checks`
- Block force pushes and branch deletion.
- Restrict direct pushes by requiring pull requests.
- Permit administrator bypass only for documented emergencies.

CODEOWNERS requests `@mikara89` for all files and repeats ownership for governance, documentation, domain, application, infrastructure, and EF migration paths. Do not enable required code-owner approval while only one maintainer exists because authors cannot approve their own pull requests.

## GitHub UI steps

1. Open `https://github.com/mikara89/open-rag/settings/rules` as a repository administrator.
2. Select **Rulesets → New ruleset → New branch ruleset**.
3. Name it `Protect main`, set enforcement to **Active**, and target the default branch or the exact branch `main`.
4. Enable **Restrict deletions** and **Block force pushes**.
5. Enable **Require a pull request before merging**.
6. Set required approvals to `0` for the current single-maintainer state. Change it to `1` when a second maintainer is available, and then enable **Dismiss stale pull request approvals when new commits are pushed**.
7. Enable **Require conversation resolution before merging**.
8. Enable **Require status checks to pass** and add `Build, test, and format` plus `Documentation checks`. Enable **Require branches to be up to date before merging** if practical.
9. Leave direct update access restricted by the pull-request rule. Add administrators to the bypass list only if emergency bypass is required by policy.
10. Save the ruleset, open a test pull request, and verify both checks and merge restrictions before relying on the rule.

GitHub's UI wording can evolve. Review the resulting ruleset summary before saving; do not assume a control is enabled merely because it was discussed in documentation.

## Optional `gh api` inspection

Read current protection without changing it:

```powershell
gh api repos/mikara89/open-rag/branches/main/protection
gh api repos/mikara89/open-rag/rulesets
```

The following classic branch-protection example is a **mutation**. Review it, set `required_approving_review_count` to `1` when a second maintainer exists, and run it only after explicit authorization:

```powershell
$protection = @{
    required_status_checks = @{
        strict = $true
        contexts = @("Build, test, and format", "Documentation checks")
    }
    enforce_admins = $false
    required_pull_request_reviews = @{
        dismiss_stale_reviews = $true
        required_approving_review_count = 0
    }
    restrictions = $null
    required_conversation_resolution = $true
    allow_force_pushes = $false
    allow_deletions = $false
} | ConvertTo-Json -Depth 5

$protection | gh api `
    --method PUT `
    -H "Accept: application/vnd.github+json" `
    -H "X-GitHub-Api-Version: 2022-11-28" `
    repos/mikara89/open-rag/branches/main/protection `
    --input -
```

This configuration leaves administrators technically able to bypass protection. The emergency procedure below governs that capability.

## Emergency administrator bypass

Use administrator bypass only when waiting for the normal PR path would materially prolong an active security or availability incident.

1. Record the incident, urgency, affected systems, decision maker, and why the normal PR path cannot be used.
2. Use the smallest possible change. Never force-push or delete `main`.
3. Run `./scripts/ci-local.ps1` before the push when feasible. If that is impossible, record why.
4. Push with an incident reference and notify the other maintainers immediately.
5. Run or observe GitHub CI on `main`; revert promptly if validation fails.
6. Open a follow-up PR or retrospective that captures the diff, review, validation evidence, and any prevention work.
