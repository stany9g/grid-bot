using System.Runtime.InteropServices;
using System.Text;

namespace GridBot.Extended.Native;

/// <summary>
/// P/Invoke declarations for the Stark native signing library.
/// Handles platform-specific library loading and marshaling between C# and native code.
/// </summary>
internal static partial class StarkNativeMethods
{
    private const string LibraryName = "stark-signer";
    private const int BufferSize = 128;

    private static IntPtr _loadedLibrary = IntPtr.Zero;
    private static string? _loadError;

    /// <summary>
    /// Error codes returned by native functions.
    /// </summary>
    public const int Success = 0;
    public const int ErrNullPointer = -1;
    public const int ErrInvalidUtf8 = -2;
    public const int ErrInvalidHex = -3;
    public const int ErrSigningFailed = -4;
    public const int ErrBufferTooSmall = -5;

    static StarkNativeMethods()
    {
        NativeLibrary.SetDllImportResolver(typeof(StarkNativeMethods).Assembly, DllImportResolver);
    }

    /// <summary>
    /// Validates that the native library can be loaded.
    /// </summary>
    /// <returns>Null if successful, error message if library cannot be loaded.</returns>
    public static string? ValidateNativeLibrary()
    {
        try
        {
            var libPath = GetNativeLibraryPath();

            Console.WriteLine($"[StarkNative] Validating native library...");
            Console.WriteLine($"[StarkNative] Platform: {RuntimeInformation.OSDescription}");
            Console.WriteLine($"[StarkNative] Architecture: {RuntimeInformation.ProcessArchitecture}");
            Console.WriteLine($"[StarkNative] Library path: {libPath}");

            if (!File.Exists(libPath))
            {
                return $"Native library not found at: {libPath}";
            }

            var fileInfo = new FileInfo(libPath);
            Console.WriteLine($"[StarkNative] File size: {fileInfo.Length} bytes");

            if (_loadedLibrary == IntPtr.Zero)
            {
                _loadedLibrary = NativeLibrary.Load(libPath);
            }

            if (_loadedLibrary == IntPtr.Zero)
            {
                return "NativeLibrary.Load returned null pointer";
            }

            Console.WriteLine($"[StarkNative] Library loaded successfully at 0x{_loadedLibrary:X}");
            return null;
        }
        catch (DllNotFoundException ex)
        {
            _loadError = $"DllNotFoundException: {ex.Message}";
            Console.WriteLine($"[StarkNative] ERROR: {_loadError}");
            return _loadError;
        }
        catch (BadImageFormatException ex)
        {
            _loadError = $"BadImageFormatException: {ex.Message}. Expected architecture: {RuntimeInformation.ProcessArchitecture}";
            Console.WriteLine($"[StarkNative] ERROR: {_loadError}");
            return _loadError;
        }
        catch (Exception ex)
        {
            _loadError = $"{ex.GetType().Name}: {ex.Message}";
            Console.WriteLine($"[StarkNative] ERROR: {_loadError}");
            return _loadError;
        }
    }

    private static IntPtr DllImportResolver(string libraryName, System.Reflection.Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName != LibraryName)
        {
            return IntPtr.Zero;
        }

        if (_loadedLibrary != IntPtr.Zero)
        {
            return _loadedLibrary;
        }

        string libPath = GetNativeLibraryPath();

        if (!File.Exists(libPath))
        {
            throw new FileNotFoundException($"Native library not found at: {libPath}");
        }

        _loadedLibrary = NativeLibrary.Load(libPath);
        return _loadedLibrary;
    }

    private static string GetNativeLibraryPath()
    {
        string baseDirectory = AppContext.BaseDirectory;
        string nativeDir = Path.Combine(baseDirectory, "Native");

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => Path.Combine(nativeDir, "stark-signer-windows-amd64.dll"),
                Architecture.Arm64 => Path.Combine(nativeDir, "stark-signer-windows-arm64.dll"),
                _ => throw new PlatformNotSupportedException($"Windows architecture {RuntimeInformation.ProcessArchitecture} is not supported")
            };
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => Path.Combine(nativeDir, "stark-signer-linux-amd64.so"),
                Architecture.Arm64 => Path.Combine(nativeDir, "stark-signer-linux-arm64.so"),
                _ => throw new PlatformNotSupportedException($"Linux architecture {RuntimeInformation.ProcessArchitecture} is not supported")
            };
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => Path.Combine(nativeDir, "stark-signer-macos-amd64.dylib"),
                Architecture.Arm64 => Path.Combine(nativeDir, "stark-signer-macos-arm64.dylib"),
                _ => throw new PlatformNotSupportedException($"macOS architecture {RuntimeInformation.ProcessArchitecture} is not supported")
            };
        }
        else
        {
            throw new PlatformNotSupportedException($"Platform {RuntimeInformation.OSDescription} is not supported");
        }
    }

    /// <summary>
    /// Signs a message hash with a Stark private key.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern int stark_sign(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string privateKey,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string messageHash,
        StringBuilder outR,
        StringBuilder outS,
        nuint outRLen,
        nuint outSLen);

    /// <summary>
    /// Derives the public key from a Stark private key.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern int stark_get_public_key(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string privateKey,
        StringBuilder outPublicKey,
        nuint outLen);

    /// <summary>
    /// Computes the Pedersen hash of two field elements.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern int stark_pedersen_hash(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string a,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string b,
        StringBuilder outHash,
        nuint outLen);

    /// <summary>
    /// Computes the Pedersen hash of multiple field elements.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern int stark_pedersen_hash_many(
        IntPtr elements,
        nuint count,
        StringBuilder outHash,
        nuint outLen);

    /// <summary>
    /// Gets the error message for a given error code.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern IntPtr stark_get_error_message(int errorCode);

    /// <summary>
    /// High-level wrapper: Signs a message hash and returns (r, s) or throws.
    /// </summary>
    public static (string R, string S) Sign(string privateKey, string messageHash)
    {
        var outR = new StringBuilder(BufferSize);
        var outS = new StringBuilder(BufferSize);

        var result = stark_sign(privateKey, messageHash, outR, outS, (nuint)BufferSize, (nuint)BufferSize);

        if (result != Success)
        {
            throw new StarkSigningException(GetErrorMessage(result), result);
        }

        return (outR.ToString(), outS.ToString());
    }

    /// <summary>
    /// High-level wrapper: Gets the public key from a private key.
    /// </summary>
    public static string GetPublicKey(string privateKey)
    {
        var outPublicKey = new StringBuilder(BufferSize);

        var result = stark_get_public_key(privateKey, outPublicKey, (nuint)BufferSize);

        if (result != Success)
        {
            throw new StarkSigningException(GetErrorMessage(result), result);
        }

        return outPublicKey.ToString();
    }

    /// <summary>
    /// High-level wrapper: Computes Pedersen hash of two elements.
    /// </summary>
    public static string PedersenHash(string a, string b)
    {
        var outHash = new StringBuilder(BufferSize);

        var result = stark_pedersen_hash(a, b, outHash, (nuint)BufferSize);

        if (result != Success)
        {
            throw new StarkSigningException(GetErrorMessage(result), result);
        }

        return outHash.ToString();
    }

    /// <summary>
    /// High-level wrapper: Computes chained Pedersen hash of multiple elements.
    /// </summary>
    public static string PedersenHashMany(params string[] elements)
    {
        if (elements.Length == 0)
        {
            return "0x0";
        }

        // Allocate array of pointers to null-terminated strings
        var ptrs = new IntPtr[elements.Length];
        try
        {
            for (int i = 0; i < elements.Length; i++)
            {
                ptrs[i] = Marshal.StringToHGlobalAnsi(elements[i]);
            }

            // Pin the pointer array and get its address
            var handle = GCHandle.Alloc(ptrs, GCHandleType.Pinned);
            try
            {
                var outHash = new StringBuilder(BufferSize);
                var result = stark_pedersen_hash_many(
                    handle.AddrOfPinnedObject(),
                    (nuint)elements.Length,
                    outHash,
                    (nuint)BufferSize);

                if (result != Success)
                {
                    throw new StarkSigningException(GetErrorMessage(result), result);
                }

                return outHash.ToString();
            }
            finally
            {
                handle.Free();
            }
        }
        finally
        {
            foreach (var ptr in ptrs)
            {
                if (ptr != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(ptr);
                }
            }
        }
    }

    /// <summary>
    /// Gets the error message for a native error code.
    /// </summary>
    public static string GetErrorMessage(int errorCode)
    {
        try
        {
            var ptr = stark_get_error_message(errorCode);
            return Marshal.PtrToStringAnsi(ptr) ?? $"Unknown error code: {errorCode}";
        }
        catch
        {
            return errorCode switch
            {
                Success => "Success",
                ErrNullPointer => "Null pointer provided",
                ErrInvalidUtf8 => "Invalid UTF-8 string",
                ErrInvalidHex => "Invalid hexadecimal value",
                ErrSigningFailed => "Signing operation failed",
                ErrBufferTooSmall => "Output buffer too small",
                _ => $"Unknown error code: {errorCode}"
            };
        }
    }
}

/// <summary>
/// Exception thrown when Stark signing operations fail.
/// </summary>
public class StarkSigningException : Exception
{
    /// <summary>
    /// Gets the native error code.
    /// </summary>
    public int ErrorCode { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="StarkSigningException"/> class.
    /// </summary>
    public StarkSigningException(string message, int errorCode)
        : base(message)
    {
        ErrorCode = errorCode;
    }
}
