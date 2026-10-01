# ARM64 7z extractor

`7zr-arm64.exe` is the ARM64 `bin/arm64/7zr.exe` binary from the
[LZMA SDK 26.03](https://www.7-zip.org/sdk.html), released by Igor Pavlov.
The LZMA SDK is public domain. The original SDK archive is available from
the linked page; this binary has only been renamed for packaging.

The Bannerlator build runs it as a separate process for 7z mod archives.
Other archive formats continue through 7th Heaven's existing extractor.
