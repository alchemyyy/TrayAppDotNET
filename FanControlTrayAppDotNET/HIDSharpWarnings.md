# HIDSharp build warnings

Captured from the final `TrayAppDotNET.slnx` Release x64 rebuild after the .NET 10/x64 conversion.

| File | Line | Code | Warning |
| --- | ---: | --- | --- |
| `HIDSharp/HidSharp/DeviceOpenUtility.cs` | 150 | `CS0618` | `HResult` is obsolete. |
| `HIDSharp/HidSharp/Platform/Libusb/NativeMethods.cs` | 93 | `CS0649` | `NativeMethods.Version.Major` is never assigned and defaults to 0. |
| `HIDSharp/HidSharp/Platform/Libusb/NativeMethods.cs` | 93 | `CS0649` | `NativeMethods.Version.Minor` is never assigned and defaults to 0. |
| `HIDSharp/HidSharp/Platform/Libusb/NativeMethods.cs` | 93 | `CS0649` | `NativeMethods.Version.Micro` is never assigned and defaults to 0. |
| `HIDSharp/HidSharp/Platform/Libusb/NativeMethods.cs` | 93 | `CS0649` | `NativeMethods.Version.Nano` is never assigned and defaults to 0. |
| `HIDSharp/HidSharp/Platform/Linux/LinuxHidManager.cs` | 72 | `CS0472` | Expression is always true because `nint` is never equal to `null`. |
| `HIDSharp/HidSharp/Platform/Linux/NativeMethods.cs` | 32 | `CS8981` | Lowercase type name `error` may become reserved. |
| `HIDSharp/HidSharp/Platform/Linux/NativeMethods.cs` | 48 | `CS8981` | Lowercase type name `oflag` may become reserved. |
| `HIDSharp/HidSharp/Platform/Linux/NativeMethods.cs` | 62 | `CS8981` | Lowercase type name `pollev` may become reserved. |
| `HIDSharp/HidSharp/Platform/Linux/NativeMethods.cs` | 72 | `CS8981` | Lowercase type name `pollfd` may become reserved. |
| `HIDSharp/HidSharp/Platform/Linux/NativeMethods.cs` | 293 | `CS8981` | Lowercase type name `termios` may become reserved. |
| `HIDSharp/HidSharp/Platform/MacOS/MacHidStream.cs` | 107 | `CS0472` | Expression is always true because `nint` is never equal to `null`. |
| `HIDSharp/HidSharp/Platform/MacOS/NativeMethods.cs` | 32 | `CS8981` | Lowercase type name `error` may become reserved. |
| `HIDSharp/HidSharp/Platform/MacOS/NativeMethods.cs` | 40 | `CS8981` | Lowercase type name `oflag` may become reserved. |
| `HIDSharp/HidSharp/Platform/MacOS/NativeMethods.cs` | 47 | `CS8981` | Lowercase type name `pollev` may become reserved. |
| `HIDSharp/HidSharp/Platform/MacOS/NativeMethods.cs` | 198 | `CS8981` | Lowercase type name `pollfd` may become reserved. |
| `HIDSharp/HidSharp/Platform/MacOS/NativeMethods.cs` | 205 | `CS8981` | Lowercase type name `termios` may become reserved. |
| `HIDSharp/HidSharp/Platform/SystemEvents.cs` | 116 | `CS8981` | Lowercase type name `timespec` may become reserved. |
| `HIDSharp/HidSharp/Platform/SystemEvents.cs` | 285 | `CS8981` | Lowercase type name `pollfd` may become reserved. |
| `HIDSharp/HidSharp/Platform/SystemEvents.cs` | 933 | `CA1416` | `FileStream.Lock(long, long)` is unsupported on macOS. |
| `HIDSharp/HidSharp/Platform/SystemEvents.cs` | 1063 | `SYSLIB0021` | `SHA256Managed` is obsolete. |
| `HIDSharp/HidSharp/Platform/SystemEvents.cs` | 1071 | `SYSLIB0021` | `SHA256Managed` is obsolete. |
| `HIDSharp/HidSharp/Platform/SystemEvents.cs` | 1425 | `SYSLIB0021` | `SHA256Managed` is obsolete. |
| `HIDSharp/HidSharp/Platform/Windows/WinBleStream.cs` | 426 | `CA2014` | `stackalloc` appears inside a loop. |
| `HIDSharp/HidSharp/Platform/Windows/WinHidStream.cs` | 116 | `CA1416` | `NativeOverlapped.EventHandle` is only supported on Windows. |
| `HIDSharp/HidSharp/Platform/Windows/WinHidStream.cs` | 180 | `CA2014` | `stackalloc` appears inside a loop. |
| `HIDSharp/HidSharp/Platform/Windows/WinHidStream.cs` | 181 | `CA1416` | `NativeOverlapped.EventHandle` is only supported on Windows. |
| `HIDSharp/HidSharp/Platform/Windows/WinSerialStream.cs` | 113 | `CA1416` | `NativeOverlapped.EventHandle` is only supported on Windows. |
| `HIDSharp/HidSharp/Platform/Windows/WinSerialStream.cs` | 140 | `CA1416` | `NativeOverlapped.EventHandle` is only supported on Windows. |
