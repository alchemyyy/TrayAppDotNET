# Embedded native helper programs in NativeAOT executables

This pattern packages a small native helper inside a Windows NativeAOT
application without running the helper in the managed runtime. The published
application remains one executable, but Windows starts that image as two
separate processes:

```text
Application.exe                    -> native wmain -> __managed__Main -> .NET application
Application.exe --native-helper    -> native wmain -> C/C++ helper loop -> exit
```

Embedding removes the production sidecar file. It does not remove process
isolation. The helper process can still have a different token, priority, locked
working set, and lifetime from the main application.

## Native entry-point dispatch

NativeAOT can delegate the executable entry point to a native static library.
That library supplies `wmain` and calls the generated `__managed__Main` only for
normal application mode:

```cpp
#if defined(EMBEDDED_NATIVE_HELPER)
extern "C" int __managed__Main(int argumentCount, wchar_t* arguments[]);

int __cdecl wmain(int argumentCount, wchar_t* arguments[])
{
    if (IsNativeHelperMode(argumentCount, arguments))
        return RunNativeHelper(argumentCount, arguments);

    return __managed__Main(argumentCount, arguments);
}
#endif
```

The mode check and helper argument parsing must remain native. Calling a
managed export, even accidentally, crosses the initialization boundary and
starts the NativeAOT runtime.

The helper branch runs after the Windows loader and C runtime startup, but
before managed runtime initialization. Provided it never calls managed code,
it has no managed heap, GC threads, finalizer thread, managed static
initialization, or UI framework startup. It is pre-managed-runtime, not
pre-CRT.

## Build wiring

Compile the helper sources as a static library for NativeAOT:

```cmake
add_library(ApplicationNativeHelper STATIC
    NativeHelper.cpp
    NativeHelperProtocol.h)

target_compile_definitions(ApplicationNativeHelper PRIVATE
    EMBEDDED_NATIVE_HELPER)
```

Link that library into the final NativeAOT image:

```xml
<PropertyGroup Condition="'$(PublishAot)' == 'true' and '$(RuntimeIdentifier)' == 'win-x64'">
  <CustomNativeMain>true</CustomNativeMain>
  <ControlFlowGuard>Guard</ControlFlowGuard>
</PropertyGroup>

<ItemGroup Condition="'$(PublishAot)' == 'true' and '$(RuntimeIdentifier)' == 'win-x64'">
  <NativeLibrary Include="$(NativeHelperLibrary)" />
</ItemGroup>
```

`NativeLibrary` is the NativeAOT linker input. `DirectPInvoke` is unnecessary
because managed code never calls the library: native `wmain` chooses either the
native helper or `__managed__Main`.

Native resources such as the application manifest and icon should normally
come from the outer executable. Do not link a second manifest into the static
library.

## Launch and IPC

The managed process prepares all communication objects, then launches
`Environment.ProcessPath` with a private mode marker and the connection data:

```text
--native-helper <parent-pid> <mapping-handle> <request-event> <response-event>
```

A practical Windows protocol is:

1. Create an anonymous shared mapping and unnamed request/response events.
2. Put a fixed-size, versioned mailbox in the mapping.
3. Start the same executable in helper mode.
4. Have the helper open the parent with `PROCESS_DUP_HANDLE` and duplicate the
   mapping and event handles into itself.
5. Monitor the parent process handle alongside the request event so the helper
   exits when its owner dies.

Duplicating handles from the parent is more robust than relying on inherited
handles across `ShellExecute`, `runas`, or UAC, and avoids globally named kernel
objects. Validate all mailbox fields, protocol versions, process IDs, and
target identities before acting on a request.

Keep the application manifest `asInvoker`. Launch through the standard user's
shell for a standard-integrity helper, and use an explicit `runas` launch for
an elevated helper. This allows one embedded implementation to support both
modes.

Complete expensive setup before publishing the ready handshake. Resolve the
emergency API entry points, adjust privileges and priority, disable execution
throttling, reserve working-set capacity, and lock the mailbox, state, hot-code
section, kernel entry-point pages, and committed stack. The steady request loop
should avoid allocation and polling.

## Non-AOT builds

`CustomNativeMain` applies to NativeAOT linking, not the normal .NET apphost.
The least disruptive development arrangement is to compile the same native
sources twice:

```cmake
add_executable(ApplicationNativeHelperSidecar WIN32
    ${NATIVE_HELPER_SOURCES}
    NativeHelper.rc)

add_library(ApplicationNativeHelper STATIC
    ${NATIVE_HELPER_SOURCES})
```

Published NativeAOT builds self-launch the combined executable. Debug and other
JIT builds launch the standalone native sidecar so debugging, hot reload, and
ordinary apphost behavior remain unchanged. Give the sidecar an `asInvoker`
manifest and use `runas` only when elevation is requested.

## PE and reliability details

Static integration changes the helper's PE properties. Recheck them instead of
assuming the standalone executable's linker settings survived.

- **Stack commitment:** If the helper locks its stack, reserve and initially
  commit enough stack in the combined image. For example, preserve a 1.5 MB
  reserve while committing 128 KB:

  ```xml
  <IlcDefaultStackSize>1572864,131072</IlcDefaultStackSize>
  ```

  This value is inserted into the linker `/STACK:` option by the current .NET
  10 toolchain. Verify the generated `link.rsp` and final PE headers after SDK
  upgrades. If stack residency is a hardening requirement, verify the actual
  number of locked bytes rather than recording only a success flag.

- **Control-flow protection:** A standalone helper built with CFG can silently
  lose that property when moved into an AOT image. Enable CFG for the complete
  NativeAOT executable and smoke-test the application afterward. Also verify
  CET compatibility, ASLR, and NX in the final image.

- **Locked hot code:** A dedicated section such as `.helperhot` can keep the
  emergency loop and its call chain together for `VirtualLock`. Confirm that
  NativeAOT did not merge or discard the section and retain it explicitly when
  linker dead-stripping could remove it.

- **Larger mapped image:** Helper mode maps the complete application PE and
  resolves its native imports even though managed code never starts. Most
  unused pages remain demand-paged, and read-only pages can be shared with the
  main process. Start and harden the helper before the system is under pressure
  rather than paying this loader cost during the emergency.

- **Native startup work:** C/C++ runtime initialization and native global
  constructors still run. Keep native initialization small and deterministic,
  and use compatible CRT/linker settings for the static library and NativeAOT
  image.

- **Shared image lifetime:** The helper keeps the main executable mapped, so
  update and replacement logic must stop it first. Both processes also have the
  same image name; track the helper by its PID and session, not by executable
  name.

For reliability-sensitive operations, keep a secondary path in the controller.
Use the native helper first, fall back only after native failure or timeout, and
start a replacement helper if the process dies.

## Verification checklist

1. Publish NativeAOT and verify that no helper sidecar enters the payload.
2. Run the combined executable directly in helper mode through a real mailbox
   smoke test.
3. Verify normal startup still reaches `__managed__Main` and helper startup
   never reaches a managed sentinel.
4. Inspect the final PE for stack reserve/commit, CFG, CET, ASLR, NX, and the
   retained hot-code section.
5. Test standard and elevated launches, UAC cancellation, helper termination,
   parent termination, protocol rejection, and controller fallback.
6. Build a JIT configuration and verify that its standalone sidecar still uses
   the same protocol.
7. Repeat the entry-point and PE checks after every .NET SDK or NativeAOT
   toolchain upgrade.

## Reference implementation

The Task Manager implementation is split across:

- `TaskManagerTrayAppDotNET/native/KillHelper/KillHelper.cpp`
- `TaskManagerTrayAppDotNET/native/KillHelper/CMakeLists.txt`
- `TaskManagerTrayAppDotNET/src/TaskManagerTrayAppDotNET.csproj`
- `TaskManagerTrayAppDotNET/src/Services/ElevatedKillHelperClient.cs`
- `TaskManagerTrayAppDotNET/src/Services/ProcessTerminationService.cs`

The underlying NativeAOT pattern is demonstrated by the pinned .NET 10
[CustomMain project](https://github.com/dotnet/runtime/blob/v10.0.9/src/tests/nativeaot/CustomMain/CustomMain.csproj)
and [native `wmain`](https://github.com/dotnet/runtime/blob/v10.0.9/src/tests/nativeaot/CustomMain/CustomMainNative.cpp).
The initialization boundary can be followed through the
[NativeAOT bootstrapper](https://github.com/dotnet/runtime/blob/v10.0.9/src/coreclr/nativeaot/Bootstrap/main.cpp)
and [managed transition implementation](https://github.com/dotnet/runtime/blob/v10.0.9/src/coreclr/nativeaot/Runtime/thread.cpp).
See Microsoft's [NativeAOT native-library documentation](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/interop)
for `NativeLibrary` linker inputs.
