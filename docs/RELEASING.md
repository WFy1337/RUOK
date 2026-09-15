# Standalone release builds

Build from the stable `main` worktree. Do not merge, copy, reset or publish pending experimental work from `feature/major-update-testing`.

## Reproduce the executable

Use the SDK selected by [global.json](../global.json) and PowerShell 7:

```powershell
git branch --show-current
git status --short
pwsh -NoProfile -File .\scripts\Publish-Standalone.ps1
```

[Publish-Standalone.ps1](../scripts/Publish-Standalone.ps1) requires committed source by default. `-AllowDirty` is only for local development validation, never the published artifact.

The script:

1. Restores the standalone dependency graph in locked mode.
2. Collects upstream license/notice files, package metadata and .NET runtime license terms from the resolved NuGet cache.
3. Extracts the missing Windows App SDK Insights resource DLL from the **same locked x64 runtime package's MSIX archive**, without installing that package or using files from the machine's WindowsApps installation.
4. Publishes an unpackaged, .NET/Windows App SDK self-contained, untrimmed, single-file x64 app.
5. Rejects output containing extra files/directories, copies the EXE to its versioned name, and writes a SHA-256 checksum.
6. Reports the exact source commit and signature status. It does **not** commit, push, tag, sign, change repository visibility or publish a GitHub release.

Default output:

```text
artifacts\releases\v1.0.0\RUOK-v1.0.0-win-x64.exe
artifacts\releases\v1.0.0\RUOK-v1.0.0-win-x64.exe.sha256
```

Only those two files are release assets. Generated support files, unpackaged diagnostic builds, license staging directories, databases, exports, PDBs and dumps must not be uploaded separately.

## Packaging details and compatibility

- Normal packaged builds retain their original identity and storage. Standalone output/intermediate directories are isolated beneath each project's `bin\Standalone` and `obj\Standalone`.
- The app uses [packages.standalone.lock.json](../src/RUOK.App/packages.standalone.lock.json) for its single-file analyzer dependency. The existing package locks and resolved dependency versions are unchanged.
- The merged PRI is named **resources.pri**, so renaming the downloaded EXE does not break native WinUI resource lookup.
- Assets resolve against the extracted application directory, not the current working directory. Standalone five-face notifications use local file URIs.
- This SDK version omits **Microsoft.WindowsAppRuntime.Insights.Resource.dll** from its normal self-contained inputs. Unpackaged notification registration otherwise fails with `0x8007007E`. The publisher bundles the exact SDK resource; the app loads it by absolute path before registration and retains it for the process lifetime. No global DLL search path or installed runtime is modified.
- The EXE automatically extracts its dependencies. Third-party notices are available in the extracted **ThirdPartyNotices** directory, including **INDEX.txt**. This is not a zero-extraction format.
- The standalone profile is independent of development/testing profiles. Existing user data is never embedded, imported or migrated.

The configuration follows Microsoft's [single-file WinUI deployment guidance](https://learn.microsoft.com/windows/apps/package-and-deploy/unpackage-winui-app#single-file-exe), [.NET single-file deployment](https://learn.microsoft.com/dotnet/core/deploying/single-file/overview), [MRT Core resource guidance](https://learn.microsoft.com/windows/apps/windows-app-sdk/mrtcore/mrtcore-overview), and [native-library loading API](https://learn.microsoft.com/dotnet/api/system.runtime.interopservices.nativelibrary.load?view=net-10.0).

## Validate before publishing

Run the applicable automated tests and the native checks in [TESTING.md](TESTING.md). Test **only the EXE** in a directory outside the source/build tree, including a path with spaces and an unrelated working directory. Check notifications, assets, tray reopening, cold activation, separate storage and the main/testing isolation.

The native runner accepts an explicit standalone path:

```powershell
.\tests\Invoke-UiSmoke.ps1 -ProcessId <pid> -StandaloneExecutable '<absolute-exe-path>' -EncouragementsOnly
.\tests\Invoke-UiSmoke.ps1 -ProcessId <pid> -StandaloneExecutable '<absolute-exe-path>' -CheckFaceSelection -CheckCompactLayout -CheckNebula
```

The runner verifies the exact executable path, lack of package identity and, for settings changes, the separate standalone database location. It cannot target the packaged/testing installation through this switch.

## Publish with approval

1. Review the intended `main` changes, tests and artifacts. Verify current remote `main`, repository visibility, and existing tags/releases.
2. Commit the source and rebuild from that exact clean commit. Do not publish a development artifact stamped with an older commit.
3. Obtain the owner's explicit approval before the GitHub push and release publication. Prefer one atomic `main` plus release-tag push; never force-push or overwrite an existing release.
4. Create the release using [RELEASE-NOTES-v1.0.0.md](RELEASE-NOTES-v1.0.0.md), upload the EXE/checksum, and verify tag target, asset sizes and SHA-256.

The first distribution is an **unsigned preview**. Preserve the documented shutdown limitation and clean-machine/ARM64/signing gaps; successful local tests are not production certification.
