# Branches and builds

Three long-lived branches keep Stable, Experimental and Alpha releases separate.

| Branch | Purpose | Automatic artifact | Data |
| --- | --- | --- | --- |
| `main` | Stable app submitted to Microsoft Store | `BasicsAdvancedTimer-MSIX-Store` (unsigned MSIX) | Windows packaged per-user local data |
| `experimental` | Everyday feature and regression testing | `BasicsAdvancedTimer-Windows-experimental` (portable ZIP) | `test-install/channels/experimental/data` |
| `alpha` | Prototypes and potentially breaking changes | `BasicsAdvancedTimer-Windows-alpha` (portable ZIP) | `test-install/channels/alpha/data` |

`main` remains the repository default and the **release gate**. The old `migration` branch and the `feature/microsoft-store-packaging` branch have been removed; their work lives in `experimental` and `main`. Do not recreate them.

The Store packaging and the release workflow are merged into `main`. Installed-app checks on a real Windows machine (first launch, persistence, tray, startup task, export, update and backup handling) are still needed before each Store submission.

## Daily workflow

1. Develop on `experimental`, or make a temporary `feature/...` branch from it for an isolated change. Use `alpha` for larger/riskier experiments.
2. Push changes; automatic tests and native smoke tests run. Select the appropriate channel using **Timer Updater.cmd**. Never test an alpha database against the experimental branch.
3. Promote an alpha change through a pull request into `experimental`. Prefer small, reviewable changes rather than merging a large unstable backlog.
4. When experimental is thoroughly tested, open a pull request from `experimental` into `main`. Review the diff and require successful checks. Merging this PR produces a Store MSIX from the exact main commit.
5. Download `BasicsAdvancedTimer-MSIX-Store` from the **successful main workflow run** in GitHub Actions, extract the ZIP and upload the enclosed `BasicsAdvancedTimer-Store-unsigned.msix` to the existing Partner Center submission. Create a Git tag for the actual submitted commit, e.g. `v1.0.1`.
6. Submit and await certification. Store installs and updates are handled through the Store; do not use the testing updater for production.

The Store workflow is deliberately restricted to `main`. Pull requests to `main` run tests **without** generating a publishable MSIX. Alpha and experimental builds never carry the registered Store identity.

## Version labels and developer tools

Stable builds show a normal version and retain the release MSIX identity. Experimental and Alpha builds show their channel beside the version and include the sample-data demo and overtime testing controls. The Store build hides demo controls and rejects the `--demo` argument; all builds retain normal version visibility.

## Safety

- Configure main branch protections under **Settings → Branches / Rulesets**: require passing automated checks, disallow force pushes and deletion, and use PRs to merge into main. Administrative access might be required to set these rules.
- Back up stored data before changing database schemas; rollback is not a schema downgrade.
- Run the installation, startup, export and data-persistence checks on the packaged build before each Store submission. The existing Partner Center submission is not changed by GitHub commits.
- The older updater's `test-install/data` tree is retained without automatic migration.
