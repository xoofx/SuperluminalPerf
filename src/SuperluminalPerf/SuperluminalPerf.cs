using System;
using System.Buffers;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

#if SUPERLUMINAL_PERF_PUBLIC
public
#else
internal
#endif
static unsafe class SuperluminalPerf
{
    private static delegate* unmanaged[Cdecl]<byte*, ushort, void> NativeSetCurrentThreadName;
    private static delegate* unmanaged[Cdecl]<byte*, ushort, byte*, ushort, uint, void> NativeBeginEvent;
    private static delegate* unmanaged[Cdecl]<char*, ushort, char*, ushort, uint, void> NativeBeginEventWide;
    private static delegate* unmanaged[Cdecl]<PerformanceAPI_SuppressTailCallOptimization> NativeEndEvent;

    public const uint Version = (2 << 16);
    
    public static bool Enabled { get; set; } = true;
    
    public static void Initialize(string? pathToPerformanceAPIDLL = null)
    {
        var localPathToPerformanceAPIDLL = pathToPerformanceAPIDLL ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Superluminal", "Performance", "API", "dll", IntPtr.Size == 8 ? "x64" : "x86", "PerformanceAPI.dll");
        if (File.Exists(localPathToPerformanceAPIDLL) && NativeLibrary.TryLoad(localPathToPerformanceAPIDLL, out var handle))
        {
            if (NativeLibrary.TryGetExport(handle, "PerformanceAPI_GetAPI", out var getApiRaw))
            {
                var getApi = (delegate* unmanaged[Cdecl]<uint, PerformanceAPI_Functions*, uint>) getApiRaw;
                PerformanceAPI_Functions functions;
                if (getApi(Version, &functions) == 1)
                {
                    NativeSetCurrentThreadName = (delegate* unmanaged[Cdecl] <byte*, ushort, void>) functions.SetCurrentThreadNameN;
                    NativeBeginEvent = (delegate* unmanaged[Cdecl]<byte*, ushort, byte*, ushort, uint, void>) functions.BeginEventN;
                    NativeBeginEventWide = (delegate* unmanaged[Cdecl]<char*, ushort, char*, ushort, uint, void>) functions.BeginEventWideN;
                    NativeEndEvent = (delegate* unmanaged[Cdecl]<PerformanceAPI_SuppressTailCallOptimization>) functions.EndEvent;
                }
            }
        }
    }

    /// <summary>
    /// Set the name of the current thread to the specified thread name.
    /// </summary>
    /// <param name="name">The thread name.</param>
    public static void SetCurrentThreadName(string name)
    {
        if (!Enabled || NativeSetCurrentThreadName == null) return;

        var byteCount = Encoding.UTF8.GetByteCount(name);
        if (byteCount <= 32)
        {
            Span<byte> localName = stackalloc byte[byteCount];
            Encoding.UTF8.GetBytes(name, localName);

            fixed (byte* pName = localName)
            {
                NativeSetCurrentThreadName(pName, (ushort)name.Length);
            }
        }
        else
        {
            var buffer = ArrayPool<byte>.Shared.Rent(byteCount);
            Encoding.UTF8.GetBytes(name, buffer);
            fixed (byte* pName = buffer)
            {
                NativeSetCurrentThreadName(pName, (ushort)name.Length);
            }
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Begin an instrumentation event with the specified ID.
    /// </summary>
    /// <param name="eventId"></param>
    /// <param name="data"></param>
    public static EventMarker BeginEvent(string eventId, string? data = null)
    {
        return BeginEvent(eventId, data, ProfilerColor.Default);
    }

    public static EventMarker BeginEvent(string eventId, string? data, ProfilerColor color)
    {
        if (Enabled && NativeBeginEventWide != null)
        {
            fixed (char* pEventId = eventId)
            fixed (char* pData = data)
            {
                NativeBeginEventWide(pEventId, (ushort) eventId.Length, pData, data == null ? (ushort) 0 : (ushort) data.Length, color.Value);
            }
        }

        return default;
    }

    public static void EndEvent()
    {
        if (Enabled && NativeEndEvent != null) NativeEndEvent();
    }

    /// <summary>
    /// Set the name of the current thread to the specified thread name.
    /// </summary>
    /// <param name="name">The thread name as an UTF8 encoded string.</param>
    /// <param name="nameCharCount"></param>
    public static void SetCurrentThreadName(ReadOnlySpan<byte> name, ushort nameCharCount)
    {
        if (!Enabled || NativeSetCurrentThreadName == null) return;
        fixed (byte* pName = name)
            NativeSetCurrentThreadName(pName, nameCharCount);
    }

    /// <summary>
    /// Starts.
    /// </summary>
    /// <param name="eventId"></param>
    /// <param name="eventCharCount"></param>
    /// <param name="data"></param>
    /// <param name="dataCharCount"></param>
    /// <param name="color"></param>
    /// <returns></returns>
    public static EventMarker BeginEvent(ReadOnlySpan<byte> eventId, ushort eventCharCount, ReadOnlySpan<byte> data, ushort dataCharCount, ProfilerColor color)
    {
        if (Enabled && NativeBeginEvent != null)
        {
            fixed (byte* pEventId = eventId)
            fixed (byte* pData = data)
            {
                NativeBeginEvent(pEventId, eventCharCount, pData, dataCharCount, color.Value);
            }
        }
        return default;
    }

    public readonly struct EventMarker : IDisposable
    {
        public void Dispose()
        {
            EndEvent();
        }
    }


    public readonly struct ProfilerColor : IEquatable<ProfilerColor>
    {
        public static readonly ProfilerColor Default = new ProfilerColor(0xFFFF_FFFF);

        public ProfilerColor(int r, int g, int b)
        {
            Value = (uint)((r << 24) | (g << 16) | (b << 8) | 0xFF);
        }

        public ProfilerColor(uint value)
        {
            Value = value;
        }

        public readonly uint Value;

        public bool Equals(ProfilerColor other)
        {
            return Value == other.Value;
        }

        public override bool Equals(object? obj)
        {
            return obj is ProfilerColor other && Equals(other);
        }

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
        public void* SetCurrentThreadName;
        public void* SetCurrentThreadNameN;
        public void* BeginEvent;
        public void* BeginEventN;
        public void* BeginEventWide;
        public void* BeginEventWideN;
        public void* EndEvent;
    }
#pragma warning restore 649
}
