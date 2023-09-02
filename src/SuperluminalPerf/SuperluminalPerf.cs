using System;
#if NET6_0_OR_GREATER
using System.Buffers;
#endif
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

/// <summary>
/// Superluminal Performance API.
/// </summary>
#if SUPERLUMINAL_PERF_PUBLIC
public
#else
internal
#endif
static unsafe class SuperluminalPerf
{
    private static delegate* unmanaged[Cdecl]<byte*, ushort, void> _nativeSetCurrentThreadName;
    private static delegate* unmanaged[Cdecl]<byte*, ushort, byte*, ushort, uint, void> _nativeBeginEvent;
    private static delegate* unmanaged[Cdecl]<char*, ushort, char*, ushort, uint, void> _nativeBeginEventWide;
    private static delegate* unmanaged[Cdecl]<PerformanceAPI_SuppressTailCallOptimization> _nativeEndEvent;
    private static bool _initialized;

    /// <summary>
    /// Version supported.
    /// </summary>
    public const uint Version = (3 << 16);

    /// <summary>
    /// Allows to enable/disable markers. Default is enabled.
    /// </summary>
    public static bool Enabled { get; set; } = true;

    /// <summary>
    /// Initialize Superluminal Performance API.
    /// </summary>
    /// <param name="pathToPerformanceAPIDLL">An optional path to the PerformanceAPI.dll</param>
    /// <remarks>
    /// This function must be called at the startup of your application.
    /// </remarks>
    public static void Initialize(string? pathToPerformanceAPIDLL = null)
    {
        if (_initialized) return;
        _initialized = true;

#if NET6_0_OR_GREATER
        // Only x86 and x64 are supported for now
        var arch = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X86 => "x86",
            Architecture.X64 => "x64",
            Architecture.Arm => "arm",
            Architecture.Arm64 => "arm64",
            Architecture.Wasm => "wasm",
            Architecture.S390x => "s390x",
            _ => throw new ArgumentOutOfRangeException()
        };
#else
        // Only x86 and x64 are supported for now
        var arch = IntPtr.Size == 8 ? "x64" : "x86";
#endif
        var localPathToPerformanceAPIDLL = pathToPerformanceAPIDLL ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Superluminal", "Performance", "API", "dll", arch, "PerformanceAPI.dll");

        if (!File.Exists(localPathToPerformanceAPIDLL) || !NativeLibrary.TryLoad(localPathToPerformanceAPIDLL, out var handle))
        {
            return;
        }

        if (NativeLibrary.TryGetExport(handle, "PerformanceAPI_GetAPI", out var getApiRaw))
        {
            var getApi = (delegate* unmanaged[Cdecl]<uint, PerformanceAPI_Functions*, uint>) getApiRaw;
            PerformanceAPI_Functions functions;
            if (getApi(Version, &functions) == 1)
            {
                _nativeSetCurrentThreadName = (delegate* unmanaged[Cdecl] <byte*, ushort, void>) functions.SetCurrentThreadNameN;
                _nativeBeginEvent = (delegate* unmanaged[Cdecl]<byte*, ushort, byte*, ushort, uint, void>) functions.BeginEventN;
                _nativeBeginEventWide = (delegate* unmanaged[Cdecl]<char*, ushort, char*, ushort, uint, void>) functions.BeginEventWideN;
                _nativeEndEvent = (delegate* unmanaged[Cdecl]<PerformanceAPI_SuppressTailCallOptimization>) functions.EndEvent;
            }
        }
    }

    /// <summary>
    /// Set the name of the current thread to the specified thread name.
    /// </summary>
    /// <param name="name">The thread name.</param>
    public static void SetCurrentThreadName(string name)
    {
        if (!Enabled || _nativeSetCurrentThreadName == null) return;

#if NET6_0_OR_GREATER
        var byteCount = Encoding.UTF8.GetByteCount(name);
        if (byteCount <= 32)
        {
            Span<byte> localName = stackalloc byte[byteCount];
            Encoding.UTF8.GetBytes(name, localName);

            fixed (byte* pName = localName)
            {
                _nativeSetCurrentThreadName(pName, (ushort)name.Length);
            }
        }
        else
        {
            var buffer = ArrayPool<byte>.Shared.Rent(byteCount);
            Encoding.UTF8.GetBytes(name, buffer);
            fixed (byte* pName = buffer)
            {
                _nativeSetCurrentThreadName(pName, (ushort)name.Length);
            }
            ArrayPool<byte>.Shared.Return(buffer);
        }
#else
        var buffer = Encoding.UTF8.GetBytes(name);
        fixed (byte* pName = buffer)
        {
            _nativeSetCurrentThreadName(pName, (ushort)name.Length);
        }
#endif
    }

    /// <summary>
    /// Begin an instrumentation event with the specified ID.
    /// </summary>
    /// <param name="eventId">The ID of this scope. The ID for a specific scope must be the same over the lifetime of the program.</param>
    /// <param name="data">The optional data for this scope. The data can vary for each invocation of this scope and is intended to hold information that is only available at runtime.</param>
    public static EventMarker BeginEvent(string eventId, string? data = null)
    {
        return BeginEvent(eventId, data, ProfilerColor.Default);
    }

    /// <summary>
    /// Begin an instrumentation event with the specified ID.
    /// </summary>
    /// <param name="eventId">The ID of this scope. The ID for a specific scope must be the same over the lifetime of the program.</param>
    /// <param name="data">The optional data for this scope. The data can vary for each invocation of this scope and is intended to hold information that is only available at runtime.</param>
    /// <param name="color">The color for this scope.</param>
    public static EventMarker BeginEvent(string eventId, string? data, ProfilerColor color)
    {
        if (Enabled && _nativeBeginEventWide != null)
        {
            fixed (char* pEventId = eventId)
            fixed (char* pData = data)
            {
                _nativeBeginEventWide(pEventId, (ushort) eventId.Length, pData, data == null ? (ushort) 0 : (ushort) data.Length, color.Value);
            }
        }

        return default;
    }

    /// <summary>
    /// End an instrumentation event. Must be matched with a call to BeginEvent within the same function.
    /// </summary>
    public static void EndEvent()
    {
        if (Enabled && _nativeEndEvent != null) _nativeEndEvent();
    }

#if NET6_0_OR_GREATER
    /// <summary>
    /// Set the name of the current thread to the specified thread name.
    /// </summary>
    /// <param name="name">The thread name as an UTF8 encoded string.</param>
    /// <param name="nameCharCount">The number of characters, not bytes, in the encoded <paramref name="name"/> string.</param>
    public static void SetCurrentThreadName(ReadOnlySpan<byte> name, ushort nameCharCount)
    {
        if (!Enabled || _nativeSetCurrentThreadName == null) return;
        fixed (byte* pName = name)
            _nativeSetCurrentThreadName(pName, nameCharCount);
    }

    /// <summary>
    /// Begin an instrumentation event with the specified ID.
    /// </summary>
    /// <param name="eventId">The ID of this scope. The ID for a specific scope must be the same over the lifetime of the program.</param>
    /// <param name="eventCharCount">The number of characters, not bytes, in the encoded <paramref name="eventId"/> string.</param>
    /// <param name="data">The optional data for this scope. The data can vary for each invocation of this scope and is intended to hold information that is only available at runtime.</param>
    /// <param name="dataCharCount">The number of characters, not bytes, in the encoded <paramref name="data"/> string.</param>
    /// <param name="color">The color for this scope.</param>
    public static EventMarker BeginEvent(ReadOnlySpan<byte> eventId, ushort eventCharCount, ReadOnlySpan<byte> data, ushort dataCharCount, ProfilerColor color)
    {
        if (Enabled && _nativeBeginEvent != null)
        {
            fixed (byte* pEventId = eventId)
            fixed (byte* pData = data)
            {
                _nativeBeginEvent(pEventId, eventCharCount, pData, dataCharCount, color.Value);
            }
        }
        return default;
    }
#endif

    /// <summary>
    /// Returned by <see cref="SuperluminalPerf.BeginEvent(string,string?)"/> and can be used with using dispose pattern.
    /// </summary>
    public readonly struct EventMarker : IDisposable
    {
        /// <inheritdoc/>
        public void Dispose()
        {
            EndEvent();
        }
    }

    /// <summary>
    /// A color for the profiler.
    /// </summary>
    public readonly struct ProfilerColor : IEquatable<ProfilerColor>
    {
        /// <summary>
        /// The default color.
        /// </summary>
        public static readonly ProfilerColor Default = new ProfilerColor(0xFFFF_FFFF);

        /// <summary>
        /// Creates a new profiler color.
        /// </summary>
        /// <param name="r">The red component.</param>
        /// <param name="g">The green component.</param>
        /// <param name="b">The blue component.</param>
        public ProfilerColor(byte r, byte g, byte b)
        {
            Value = (uint)((r << 24) | (g << 16) | (b << 8) | 0xFF);
        }

        /// <summary>
        /// Creates a new profiler color.
        /// </summary>
        /// <param name="value">The color value.</param>
        public ProfilerColor(uint value)
        {
            Value = value;
        }

        /// <summary>
        /// The color value.
        /// </summary>
        public readonly uint Value;

        /// <inheritdoc/>
        public bool Equals(ProfilerColor other)
        {
            return Value == other.Value;
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            return obj is ProfilerColor other && Equals(other);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return (int)Value;
        }

        public static bool operator ==(ProfilerColor left, ProfilerColor right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(ProfilerColor left, ProfilerColor right)
        {
            return !left.Equals(right);
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"#{Value:X8}";
        }
    }

#pragma warning disable 649
    /// <summary>
    /// Helper struct that is used to prevent calls to EndEvent from being optimized to jmp instructions as part of tail call optimization.
    /// You don't ever need to do anything with this as user of the API.
    /// </summary>
    private struct PerformanceAPI_SuppressTailCallOptimization
    {
        public long Value1;
        public long Value2;
        public long Value3;
    }

    private unsafe struct PerformanceAPI_Functions
    {
        // API 2.0
        public void* SetCurrentThreadName;
        public void* SetCurrentThreadNameN;
        public void* BeginEvent;
        public void* BeginEventN;
        public void* BeginEventWide;
        public void* BeginEventWideN;
        public void* EndEvent;

        // API 3.0 (We don't expose them)
        public void* RegisterFiber;
        public void* UnregisterFiber;
        public void* BeginFiberSwitch;
        public void* EndFiberSwitch;
    }
#pragma warning restore 649


#if NETSTANDARD2_0
    private static class NativeLibrary
    {
        public static bool TryLoad(string path, out IntPtr handle)
        {
            handle = LoadLibrary(path);
            return handle != IntPtr.Zero;
        }

        public static bool TryGetExport(IntPtr handle, string name, out IntPtr entryPtr)
        {
            entryPtr = GetProcAddress(handle, name);
            return entryPtr != IntPtr.Zero;
        }


        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string libraryName);

        [DllImport("kernel32", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);
    }
#endif
}
