# Native SDK

`linux-arm64` contains the C++ algorithm library and its source headers for RK3588.

## Updating the SDK

1. Replace the `.so` and header files under `Native/linux-arm64`.
2. Compare the exported API and structures with the existing headers.
3. If the ABI changed, regenerate every file under `SDK/Generated` using `interop-generation.json`.
4. Do not place business logic in generated files. Keep compatibility handling in `Core/Impl/NativeAlgorithmService.cs`.
5. Publish for `linux-arm64`, copy the whole publish directory to the board, and run `ldd ./libDemo.so` before starting the service.

When only the library file name changes and the ABI stays compatible, update `NativeSdk:LibraryName` in `appsettings.json`; regenerating P/Invoke declarations is not required.
