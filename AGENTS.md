# Repository instructions

## Releases

In this repository, a request to create a release means only building the Microsoft Store MSIX package locally on Windows with `scripts/Build-StoreRelease.ps1 -Version <X.Y.Z> -RequireWindowsOcr`. The deliverable is the local `.msix` and its checksum under `artifacts/store/<X.Y.Z>/`.

Do not infer a request to edit source versions or release notes, commit, pull, merge, push, tag, create a GitHub release, run a GitHub workflow, upload the package, or submit it to Partner Center from the word "release". Perform any of those actions only when the user separately requests that specific action. Never use GitHub Actions to build or test this repository.
