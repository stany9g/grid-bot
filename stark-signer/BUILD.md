# Building the Stark Signer Native Library

This document describes how to build the `stark-signer` native library for different platforms.

## Prerequisites

1. **Rust toolchain** (1.70+)
   ```bash
   curl --proto '=https' --tlsv1.2 -sSf https://sh.rustup.rs | sh
   ```

2. **Cross-compilation targets** (for building on different platforms)
   ```bash
   # Windows x64
   rustup target add x86_64-pc-windows-msvc

   # Linux x64
   rustup target add x86_64-unknown-linux-gnu

   # Linux ARM64
   rustup target add aarch64-unknown-linux-gnu

   # macOS ARM64 (Apple Silicon)
   rustup target add aarch64-apple-darwin

   # macOS x64 (Intel)
   rustup target add x86_64-apple-darwin
   ```

## Building

### Windows x64 (native on Windows)

```powershell
cd stark-signer
cargo build --release --target x86_64-pc-windows-msvc

# Copy to GridBot.Extended
copy target\x86_64-pc-windows-msvc\release\stark_signer.dll ..\GridBot.Extended\Native\stark-signer-windows-amd64.dll
```

### Linux x64 (native on Linux)

```bash
cd stark-signer
cargo build --release --target x86_64-unknown-linux-gnu

# Copy to GridBot.Extended
cp target/x86_64-unknown-linux-gnu/release/libstark_signer.so ../GridBot.Extended/Native/stark-signer-linux-amd64.so
```

### Linux ARM64 (cross-compile or native)

```bash
cd stark-signer

# If cross-compiling from x64, install the linker:
# sudo apt install gcc-aarch64-linux-gnu

cargo build --release --target aarch64-unknown-linux-gnu

# Copy to GridBot.Extended
cp target/aarch64-unknown-linux-gnu/release/libstark_signer.so ../GridBot.Extended/Native/stark-signer-linux-arm64.so
```

### macOS ARM64 (Apple Silicon)

```bash
cd stark-signer
cargo build --release --target aarch64-apple-darwin

# Copy to GridBot.Extended
cp target/aarch64-apple-darwin/release/libstark_signer.dylib ../GridBot.Extended/Native/stark-signer-macos-arm64.dylib
```

### macOS x64 (Intel)

```bash
cd stark-signer
cargo build --release --target x86_64-apple-darwin

# Copy to GridBot.Extended
cp target/x86_64-apple-darwin/release/libstark_signer.dylib ../GridBot.Extended/Native/stark-signer-macos-amd64.dylib
```

## Build All Platforms (CI/CD)

For GitHub Actions or other CI systems, you can build all platforms:

```yaml
# .github/workflows/build-stark-signer.yml
name: Build Stark Signer

on:
  push:
    paths:
      - 'stark-signer/**'

jobs:
  build-windows:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
      - uses: dtolnay/rust-toolchain@stable
      - run: cargo build --release --manifest-path stark-signer/Cargo.toml
      - uses: actions/upload-artifact@v4
        with:
          name: stark-signer-windows-amd64
          path: stark-signer/target/release/stark_signer.dll

  build-linux:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: dtolnay/rust-toolchain@stable
      - run: cargo build --release --manifest-path stark-signer/Cargo.toml
      - uses: actions/upload-artifact@v4
        with:
          name: stark-signer-linux-amd64
          path: stark-signer/target/release/libstark_signer.so

  build-macos:
    runs-on: macos-latest
    steps:
      - uses: actions/checkout@v4
      - uses: dtolnay/rust-toolchain@stable
      - run: cargo build --release --manifest-path stark-signer/Cargo.toml
      - uses: actions/upload-artifact@v4
        with:
          name: stark-signer-macos-arm64
          path: stark-signer/target/release/libstark_signer.dylib
```

## Testing

Run the Rust unit tests:

```bash
cd stark-signer
cargo test
```

## Output File Locations

After building, the native libraries should be placed in:

```
GridBot.Extended/
└── Native/
    ├── stark-signer-windows-amd64.dll   # Windows x64
    ├── stark-signer-linux-amd64.so      # Linux x64
    ├── stark-signer-linux-arm64.so      # Linux ARM64
    ├── stark-signer-macos-amd64.dylib   # macOS Intel
    └── stark-signer-macos-arm64.dylib   # macOS Apple Silicon
```

## Troubleshooting

### "DllNotFoundException" at runtime
- Ensure the native library is in the `Native/` directory relative to the executable
- Check architecture matches (x64 binary requires x64 library)
- On Linux, ensure `libstdc++` and `libgcc` are installed

### "BadImageFormatException"
- Architecture mismatch (e.g., trying to load x64 library from ARM64 process)
- Rebuild for the correct target

### Signing failures
- Verify the private key is valid hex (with or without 0x prefix)
- Ensure message hash is properly formatted
