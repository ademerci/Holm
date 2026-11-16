using System;
using System.Runtime.InteropServices;

namespace Holm.Platform.MacOS;

/// <summary>
/// The Objective-C runtime, called directly: objc_msgSend with one exact signature per shape of call (on arm64 a
/// message must be sent with the receiver's real prototype, never through varargs), classes made at run time for
/// the few callbacks AppKit only delivers to objects, and blocks built by hand for the APIs that take one.
/// </summary>
static unsafe class ObjC
{
    const string Lib = "/usr/lib/libobjc.A.dylib";

    [DllImport(Lib)] public static extern IntPtr objc_getClass(string name);
    [DllImport(Lib)] public static extern IntPtr sel_registerName(string name);
    [DllImport(Lib)] public static extern IntPtr objc_allocateClassPair(IntPtr superclass, string name, nint extraBytes);
    [DllImport(Lib)] public static extern void objc_registerClassPair(IntPtr cls);
    [DllImport(Lib)] public static extern byte class_addMethod(IntPtr cls, IntPtr selector, IntPtr implementation, string types);
    [DllImport(Lib)] public static extern IntPtr objc_autoreleasePoolPush();
    [DllImport(Lib)] public static extern void objc_autoreleasePoolPop(IntPtr pool);

    [DllImport(Lib, EntryPoint = "objc_msgSend")] public static extern IntPtr Send(IntPtr receiver, IntPtr selector);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] public static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr a);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] public static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr a, IntPtr b, IntPtr c, IntPtr d);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] public static extern IntPtr Send(IntPtr receiver, IntPtr selector, ulong mask, IntPtr block);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] public static extern IntPtr SendLong(IntPtr receiver, IntPtr selector, nint a);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] public static extern IntPtr SendULong(IntPtr receiver, IntPtr selector, nuint a);
    /// <summary>BOOL arguments and results are one byte on both Intel and Apple silicon.</summary>
    [DllImport(Lib, EntryPoint = "objc_msgSend")] public static extern IntPtr SendBool(IntPtr receiver, IntPtr selector, byte a);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] public static extern byte SendReturnBool(IntPtr receiver, IntPtr selector);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] public static extern byte SendReturnBool(IntPtr receiver, IntPtr selector, IntPtr a);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] public static extern nint SendReturnLong(IntPtr receiver, IntPtr selector);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] public static extern double SendReturnDouble(IntPtr receiver, IntPtr selector);
    /// <summary>Two doubles come back in registers on both architectures, so plain objc_msgSend is right.</summary>
    [DllImport(Lib, EntryPoint = "objc_msgSend")] public static extern NSPoint SendReturnPoint(IntPtr receiver, IntPtr selector);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] static extern NSRect SendReturnRectArm(IntPtr receiver, IntPtr selector);
    [DllImport(Lib, EntryPoint = "objc_msgSend_stret")] static extern void SendReturnRectIntel(out NSRect result, IntPtr receiver, IntPtr selector);

    /// <summary>A 32-byte struct comes back in registers on Apple silicon and through memory on Intel.</summary>
    public static NSRect SendReturnRect(IntPtr receiver, IntPtr selector)
    {
        if (RuntimeInformation.ProcessArchitecture == Architecture.X64)
        {
            SendReturnRectIntel(out var rect, receiver, selector);
            return rect;
        }
        return SendReturnRectArm(receiver, selector);
    }

    public static IntPtr Class(string name) => objc_getClass(name);
    public static IntPtr Sel(string name) => sel_registerName(name);

    /// <summary>An autoreleased NSString.</summary>
    public static IntPtr String(string text)
    {
        IntPtr utf8 = Marshal.StringToCoTaskMemUTF8(text);
        try { return Send(Class("NSString"), Sel("stringWithUTF8String:"), utf8); }
        finally { Marshal.FreeCoTaskMem(utf8); }
    }

    /// <summary>The text of an NSString; empty for nil.</summary>
    public static string ToText(IntPtr nsString)
    {
        if (nsString == IntPtr.Zero) return "";
        IntPtr utf8 = Send(nsString, Sel("UTF8String"));
        return Marshal.PtrToStringUTF8(utf8) ?? "";
    }

    static IntPtr _globalBlockClass;

    /// <summary>
    /// A block that captures nothing, calling <paramref name="invoke"/> (whose first argument is the block
    /// itself). Global blocks are never copied or freed by the runtime; this one lives as long as the app.
    /// </summary>
    public static IntPtr GlobalBlock(IntPtr invoke)
    {
        if (_globalBlockClass == IntPtr.Zero)
            _globalBlockClass = NativeLibrary.GetExport(NativeLibrary.Load("/usr/lib/libSystem.B.dylib"), "_NSConcreteGlobalBlock");
        var descriptor = (BlockDescriptor*)NativeMemory.AllocZeroed((nuint)sizeof(BlockDescriptor));
        descriptor->Size = (nuint)sizeof(BlockLiteral);
        var block = (BlockLiteral*)NativeMemory.AllocZeroed((nuint)sizeof(BlockLiteral));
        block->Isa = _globalBlockClass;
        block->Flags = 1 << 28; // BLOCK_IS_GLOBAL
        block->Invoke = invoke;
        block->Descriptor = descriptor;
        return (IntPtr)block;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct BlockDescriptor { public nuint Reserved, Size; }

    [StructLayout(LayoutKind.Sequential)]
    struct BlockLiteral { public IntPtr Isa; public int Flags, Reserved; public IntPtr Invoke; public BlockDescriptor* Descriptor; }
}

[StructLayout(LayoutKind.Sequential)]
public struct NSPoint { public double X, Y; }

[StructLayout(LayoutKind.Sequential)]
public struct NSRect { public double X, Y, Width, Height; }

/// <summary>CoreFoundation: the values IOKit and CoreAudio hand back.</summary>
static class CF
{
    const string Lib = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    const int Utf8 = 0x08000100;
    const int NumberSInt64 = 4, NumberDouble = 13;

    [DllImport(Lib)] public static extern void CFRelease(IntPtr value);
    [DllImport(Lib)] public static extern nint CFArrayGetCount(IntPtr array);
    [DllImport(Lib)] public static extern IntPtr CFArrayGetValueAtIndex(IntPtr array, nint index);
    [DllImport(Lib)] public static extern IntPtr CFDictionaryGetValue(IntPtr dictionary, IntPtr key);
    [DllImport(Lib)] static extern IntPtr CFStringCreateWithCString(IntPtr allocator, string text, int encoding);
    [DllImport(Lib)] static extern byte CFStringGetCString(IntPtr text, byte[] buffer, nint size, int encoding);
    [DllImport(Lib)] static extern byte CFNumberGetValue(IntPtr number, int type, out long value);
    [DllImport(Lib)] static extern byte CFNumberGetValue(IntPtr number, int type, out double value);
    [DllImport(Lib)] static extern nuint CFGetTypeID(IntPtr value);
    [DllImport(Lib)] static extern nuint CFBooleanGetTypeID();
    [DllImport(Lib)] static extern nuint CFNumberGetTypeID();
    [DllImport(Lib)] static extern nuint CFStringGetTypeID();
    [DllImport(Lib)] static extern byte CFBooleanGetValue(IntPtr boolean);
    [DllImport(Lib)] public static extern IntPtr CFRunLoopGetMain();
    [DllImport(Lib)] public static extern void CFRunLoopAddSource(IntPtr loop, IntPtr source, IntPtr mode);
    [DllImport(Lib)] public static extern void CFRunLoopRemoveSource(IntPtr loop, IntPtr source, IntPtr mode);

    /// <summary>kCFRunLoopDefaultMode, a constant CFString exported by the framework.</summary>
    public static IntPtr DefaultMode => Marshal.ReadIntPtr(NativeLibrary.GetExport(NativeLibrary.Load(Lib), "kCFRunLoopDefaultMode"));

    /// <summary>A CFString the caller releases.</summary>
    public static IntPtr Str(string text) => CFStringCreateWithCString(IntPtr.Zero, text, Utf8);

    public static IntPtr Get(IntPtr dictionary, string key)
    {
        IntPtr k = Str(key);
        try { return CFDictionaryGetValue(dictionary, k); }
        finally { CFRelease(k); }
    }

    public static string? Text(IntPtr value)
    {
        if (value == IntPtr.Zero || CFGetTypeID(value) != CFStringGetTypeID()) return null;
        var buffer = new byte[1024];
        return CFStringGetCString(value, buffer, buffer.Length, Utf8) != 0
            ? System.Text.Encoding.UTF8.GetString(buffer, 0, Array.IndexOf(buffer, (byte)0) is var end and >= 0 ? end : buffer.Length)
            : null;
    }

    public static double? Number(IntPtr value)
    {
        if (value == IntPtr.Zero) return null;
        nuint type = CFGetTypeID(value);
        if (type == CFBooleanGetTypeID()) return CFBooleanGetValue(value) != 0 ? 1 : 0;
        if (type != CFNumberGetTypeID()) return null;
        return CFNumberGetValue(value, NumberDouble, out double d) != 0 ? d : null;
    }

    public static bool? Bool(IntPtr value)
    {
        if (value == IntPtr.Zero) return null;
        if (CFGetTypeID(value) == CFBooleanGetTypeID()) return CFBooleanGetValue(value) != 0;
        return Number(value) is { } n ? n != 0 : null;
    }
}
