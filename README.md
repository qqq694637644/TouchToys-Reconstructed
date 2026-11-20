# TouchToys Reconstruction

Independent C# reconstruction of the supplied TouchToys 0.8.54.0 Windows package, intended as a buildable starting point for further development.

> This is a reverse-engineered reconstruction, not the original source repository. It is not affiliated with the original publisher.

## Technology

- C# / .NET 8
- WinUI 3 and Windows App SDK 1.8
- Windows SDK 10.0.22621
- MSIX packaging
- x64 and ARM64 targets

The main application UI and behavior are implemented in C#. The supplied package did not contain the original XAML source, so the compiled `App.xbf` resource is retained and copied as the `App.xaml` package resource during build. The rest of the recovered C# project is editable source.

## Build

On Windows with the .NET 8 SDK and Windows 10/11 SDK installed:

```powershell
dotnet publish .\src\TouchToys\TouchToys.csproj -c Release -r win-x64 --self-contained true
```

Create an MSIX package:

```powershell
.scripts\Build-Msix.ps1 -Architecture x64
.scripts\Build-Msix.ps1 -Architecture arm64
```

The script writes packages under `artifacts/`. The production manifest in `src/TouchToys/Package/AppxManifest.xml` retains the `uiAccess` capability. Production installation therefore requires a certificate trusted for that capability; no signing certificate or private key is included in this repository.

## CI

GitHub Actions builds both architectures. The x64 job signs a CI-only MSIX with an ephemeral certificate, installs it on the Windows runner, runs the built-in UI/touch smoke suite, and uploads its JSON report and PNG screenshots as workflow artifacts. The CI manifest omits the restricted `uiAccess` capability so a temporary test certificate can be used safely.

## Reconstruction notes

- The main assembly was decompiled with ILSpyCmd 9.1.0.7988.
- Compiler-generated WinRT generic helper files are excluded from compilation because the WinRT source generator recreates them.
- Several decompiler defects were manually corrected, including touch-contact coordinate deltas, native pointer message constants, and a QR scanner definite-assignment artifact.
- The original MSIX bundle, test certificate, private signing material, and original runtime binaries are not included.

See [the reverse-engineering notes](docs/REVERSE_ENGINEERING.md) for evidence, dependencies, and known gaps.

## License

No open-source license has been assigned. Public visibility does not by itself grant permission to reuse or redistribute the reconstructed code or included assets. Confirm licensing with the repository owner before reuse.
