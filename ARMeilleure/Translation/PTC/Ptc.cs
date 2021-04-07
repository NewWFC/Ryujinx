using ARMeilleure.CodeGen;
using ARMeilleure.CodeGen.Unwinding;
using ARMeilleure.CodeGen.X86;
using ARMeilleure.Memory;
using ARMeilleure.Translation.Cache;
using Ryujinx.Common;
using Ryujinx.Common.Configuration;
using Ryujinx.Common.Logging;
using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

using static ARMeilleure.Translation.PTC.PtcFormatter;

namespace ARMeilleure.Translation.PTC
{
    public static class Ptc
    {
        private const string HeaderMagicString = "PTChd\0\0\0";

        private const uint InternalVersion = 2168; //! To be incremented manually for each change to the ARMeilleure project.

        private const string ActualDir = "0";
        private const string BackupDir = "1";

        private const string TitleIdTextDefault = "0000000000000000";
        private const string DisplayVersionDefault = "0";

        internal const int PageTablePointerIndex = -1; // Must be a negative value.
        internal const int JumpPointerIndex = -2; // Must be a negative value.
        internal const int DynamicPointerIndex = -3; // Must be a negative value.

        private const byte FillingByte = 0x00;
        private const CompressionLevel SaveCompressionLevel = CompressionLevel.Fastest;

        // Carriers.
        private static MemoryStream _infosStream;
        private static List<byte[]> _codesList;
        private static MemoryStream _relocsStream;
        private static MemoryStream _unwindInfosStream;

        private static BinaryWriter _infosWriter;

        private static readonly ulong _headerMagic;

        private static readonly ManualResetEvent _waitEvent;

        private static readonly object _lock;

        private static bool _disposed;

        internal static PtcJumpTable PtcJumpTable { get; private set; }

        internal static string TitleIdText { get; private set; }
        internal static string DisplayVersion { get; private set; }

        internal static string CachePathActual { get; private set; }
        internal static string CachePathBackup { get; private set; }

        internal static PtcState State { get; private set; }

        // Progress reporting helpers.
        private static volatile int _translateCount;
        private static volatile int _translateTotalCount;
        public static event Action<PtcLoadingState, int, int> PtcStateChanged;

        static Ptc()
        {
            InitializeCarriers();

            _headerMagic = BinaryPrimitives.ReadUInt64LittleEndian(EncodingCache.UTF8NoBOM.GetBytes(HeaderMagicString).AsSpan());

            _waitEvent = new ManualResetEvent(true);

            _lock = new object();

            _disposed = false;

            PtcJumpTable = new PtcJumpTable();

            TitleIdText = TitleIdTextDefault;
            DisplayVersion = DisplayVersionDefault;

            CachePathActual = string.Empty;
            CachePathBackup = string.Empty;

            Disable();
        }

        public static void Initialize(string titleIdText, string displayVersion, bool enabled)
        {
            Wait();

            PtcProfiler.Wait();
            PtcProfiler.ClearEntries();

            Logger.Info?.Print(LogClass.Ptc, $"Initializing Profiled Persistent Translation Cache (enabled: {enabled}).");

            if (!enabled || string.IsNullOrEmpty(titleIdText) || titleIdText == TitleIdTextDefault)
            {
                TitleIdText = TitleIdTextDefault;
                DisplayVersion = DisplayVersionDefault;

                CachePathActual = string.Empty;
                CachePathBackup = string.Empty;

                Disable();

                return;
            }

            TitleIdText = titleIdText;
            DisplayVersion = !string.IsNullOrEmpty(displayVersion) ? displayVersion : DisplayVersionDefault;

            string workPathActual = Path.Combine(AppDataManager.GamesDirPath, TitleIdText, "cache", "cpu", ActualDir);
            string workPathBackup = Path.Combine(AppDataManager.GamesDirPath, TitleIdText, "cache", "cpu", BackupDir);

            if (!Directory.Exists(workPathActual))
            {
                Directory.CreateDirectory(workPathActual);
            }

            if (!Directory.Exists(workPathBackup))
            {
                Directory.CreateDirectory(workPathBackup);
            }

            CachePathActual = Path.Combine(workPathActual, DisplayVersion);
            CachePathBackup = Path.Combine(workPathBackup, DisplayVersion);

            PreLoad();
            PtcProfiler.PreLoad();

            Enable();
        }

        private static void InitializeCarriers()
        {
            _infosStream = new MemoryStream();
            _codesList = new List<byte[]>();
            _relocsStream = new MemoryStream();
            _unwindInfosStream = new MemoryStream();

            _infosWriter = new BinaryWriter(_infosStream, EncodingCache.UTF8NoBOM, true);
        }

        private static void DisposeCarriers()
        {
            _infosWriter.Dispose();

            _infosStream.Dispose();
            _codesList.Clear();
            _relocsStream.Dispose();
            _unwindInfosStream.Dispose();
        }

        private static bool AreCarriersEmpty()
        {
            return _infosStream.Length == 0L && _codesList.Count == 0 && _relocsStream.Length == 0L && _unwindInfosStream.Length == 0L;
        }

        private static void ResetCarriersIfNeeded()
        {
            if (AreCarriersEmpty())
            {
                return;
            }

            DisposeCarriers();

            InitializeCarriers();
        }

        private static void PreLoad()
        {
            string fileNameActual = string.Concat(CachePathActual, ".cache");
            string fileNameBackup = string.Concat(CachePathBackup, ".cache");

            FileInfo fileInfoActual = new FileInfo(fileNameActual);
            FileInfo fileInfoBackup = new FileInfo(fileNameBackup);

            if (fileInfoActual.Exists && fileInfoActual.Length != 0L)
            {
                if (!Load(fileNameActual, false))
                {
                    if (fileInfoBackup.Exists && fileInfoBackup.Length != 0L)
                    {
                        Load(fileNameBackup, true);
                    }
                }
            }
            else if (fileInfoBackup.Exists && fileInfoBackup.Length != 0L)
            {
                Load(fileNameBackup, true);
            }
        }

        private static unsafe bool Load(string fileName, bool isBackup)
        {
            using (FileStream compressedStream = new(fileName, FileMode.Open))
            using (DeflateStream deflateStream = new(compressedStream, CompressionMode.Decompress, true))
            {
                Hash128 currentSizeHash = DeserializeStructure<Hash128>(compressedStream);

                Span<byte> sizeBytes = new byte[sizeof(long)];
                compressedStream.Read(sizeBytes);
                Hash128 expectedSizeHash = XXHash128.ComputeHash(sizeBytes);

                if (currentSizeHash != expectedSizeHash)
                {
                    InvalidateCompressedStream(compressedStream);

                    return false;
                }

                long size = BinaryPrimitives.ReadInt64LittleEndian(sizeBytes);

                IntPtr intPtr = IntPtr.Zero;

                try
                {
                    intPtr = Marshal.AllocHGlobal(new IntPtr(size));

                    using (UnmanagedMemoryStream stream = new((byte*)intPtr.ToPointer(), size, size, FileAccess.ReadWrite))
                    {
                        try
                        {
                            deflateStream.CopyTo(stream);
                        }
                        catch
                        {
                            InvalidateCompressedStream(compressedStream);

                            return false;
                        }

                        Debug.Assert(stream.Position == stream.Length);

                        stream.Seek(0L, SeekOrigin.Begin);

                        Header header = DeserializeStructure<Header>(stream);

                        if (header.Magic != _headerMagic)
                        {
                            InvalidateCompressedStream(compressedStream);

                            return false;
                        }

                        if (header.CacheFileVersion != InternalVersion)
                        {
                            InvalidateCompressedStream(compressedStream);

                            return false;
                        }

                        if (header.Endianness != GetEndianness())
                        {
                            InvalidateCompressedStream(compressedStream);

                            return false;
                        }

                        if (header.FeatureInfo != GetFeatureInfo())
                        {
                            InvalidateCompressedStream(compressedStream);

                            return false;
                        }

                        if (header.OSPlatform != GetOSPlatform())
                        {
                            InvalidateCompressedStream(compressedStream);

                            return false;
                        }

                        ReadOnlySpan<byte> infosBytes = new(stream.PositionPointer, header.InfosLength);
                        stream.Seek(header.InfosLength, SeekOrigin.Current);

                        Hash128 infosHash = XXHash128.ComputeHash(infosBytes);

                        if (header.InfosHash != infosHash)
                        {
                            InvalidateCompressedStream(compressedStream);

                            return false;
                        }

                        ReadOnlySpan<byte> codesBytes = (int)header.CodesLength > 0 ? new(stream.PositionPointer, (int)header.CodesLength) : ReadOnlySpan<byte>.Empty;
                        stream.Seek(header.CodesLength, SeekOrigin.Current);

                        Hash128 codesHash = XXHash128.ComputeHash(codesBytes);

                        if (header.CodesHash != codesHash)
                        {
                            InvalidateCompressedStream(compressedStream);

                            return false;
                        }

                        ReadOnlySpan<byte> relocsBytes = new(stream.PositionPointer, header.RelocsLength);
                        stream.Seek(header.RelocsLength, SeekOrigin.Current);

                        Hash128 relocsHash = XXHash128.ComputeHash(relocsBytes);

                        if (header.RelocsHash != relocsHash)
                        {
                            InvalidateCompressedStream(compressedStream);

                            return false;
                        }

                        ReadOnlySpan<byte> unwindInfosBytes = new(stream.PositionPointer, header.UnwindInfosLength);
                        stream.Seek(header.UnwindInfosLength, SeekOrigin.Current);

                        Hash128 unwindInfosHash = XXHash128.ComputeHash(unwindInfosBytes);

                        if (header.UnwindInfosHash != unwindInfosHash)
                        {
                            InvalidateCompressedStream(compressedStream);

                            return false;
                        }

                        ReadOnlySpan<byte> ptcJumpTableBytes = new(stream.PositionPointer, header.PtcJumpTableLength);
                        stream.Seek(header.PtcJumpTableLength, SeekOrigin.Current);

                        Hash128 ptcJumpTableHash = XXHash128.ComputeHash(ptcJumpTableBytes);

                        if (header.PtcJumpTableHash != ptcJumpTableHash)
                        {
                            InvalidateCompressedStream(compressedStream);

                            return false;
                        }

                        Debug.Assert(stream.Position == stream.Length);

                        stream.Seek((long)Unsafe.SizeOf<Header>(), SeekOrigin.Begin);

                        _infosStream.Write(infosBytes);
                        stream.Seek(header.InfosLength, SeekOrigin.Current);

                        _codesList.ReadFrom(stream);

                        _relocsStream.Write(relocsBytes);
                        stream.Seek(header.RelocsLength, SeekOrigin.Current);

                        _unwindInfosStream.Write(unwindInfosBytes);
                        stream.Seek(header.UnwindInfosLength, SeekOrigin.Current);

                        PtcJumpTable = PtcJumpTable.Deserialize(stream);

                        Debug.Assert(stream.Position == stream.Length);
                    }
                }
                finally
                {
                    if (intPtr != IntPtr.Zero)
                    {
                        Marshal.FreeHGlobal(intPtr);
                    }
                }
            }

            long fileSize = new FileInfo(fileName).Length;

            Logger.Info?.Print(LogClass.Ptc, $"{(isBackup ? "Loaded Backup Translation Cache" : "Loaded Translation Cache")} (size: {fileSize} bytes, translated functions: {GetEntriesCount()}).");

            return true;
        }

        private static void InvalidateCompressedStream(FileStream compressedStream)
        {
            compressedStream.SetLength(0L);
        }

        private static void PreSave()
        {
            _waitEvent.Reset();

            try
            {
                string fileNameActual = string.Concat(CachePathActual, ".cache");
                string fileNameBackup = string.Concat(CachePathBackup, ".cache");

                FileInfo fileInfoActual = new FileInfo(fileNameActual);

                if (fileInfoActual.Exists && fileInfoActual.Length != 0L)
                {
                    File.Copy(fileNameActual, fileNameBackup, true);
                }

                Save(fileNameActual);
            }
            finally
            {
                ResetCarriersIfNeeded();
                PtcJumpTable.ClearIfNeeded();

                GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            }

            _waitEvent.Set();
        }

        private static unsafe void Save(string fileName)
        {
            int translatedFuncsCount;

            int headerSize = Unsafe.SizeOf<Header>();

            Header header = new Header()
            {
                Magic = _headerMagic,

                CacheFileVersion = InternalVersion,
                Endianness = GetEndianness(),
                FeatureInfo = GetFeatureInfo(),
                OSPlatform = GetOSPlatform(),

                InfosLength = (int)_infosStream.Length,
                CodesLength = _codesList.Length(),
                RelocsLength = (int)_relocsStream.Length,
                UnwindInfosLength = (int)_unwindInfosStream.Length,
                PtcJumpTableLength = PtcJumpTable.GetSerializeSize(PtcJumpTable)
            };

            long size = (long)headerSize + header.InfosLength + header.CodesLength + header.RelocsLength + header.UnwindInfosLength + header.PtcJumpTableLength;

            Span<byte> sizeBytes = new byte[sizeof(long)];
            BinaryPrimitives.WriteInt64LittleEndian(sizeBytes, size);
            Hash128 sizeHash = XXHash128.ComputeHash(sizeBytes);

            Span<byte> sizeHashBytes = new byte[Unsafe.SizeOf<Hash128>()];
            MemoryMarshal.Write<Hash128>(sizeHashBytes, ref sizeHash);

            IntPtr intPtr = IntPtr.Zero;

            try
            {
                intPtr = Marshal.AllocHGlobal(new IntPtr(size));

                using (UnmanagedMemoryStream stream = new((byte*)intPtr.ToPointer(), size, size, FileAccess.ReadWrite))
                {
                    stream.Seek((long)headerSize, SeekOrigin.Begin);

                    ReadOnlySpan<byte> infosBytes = new(stream.PositionPointer, header.InfosLength);
                    _infosStream.WriteTo(stream);

                    ReadOnlySpan<byte> codesBytes = (int)header.CodesLength > 0 ? new(stream.PositionPointer, (int)header.CodesLength) : ReadOnlySpan<byte>.Empty;
                    _codesList.WriteTo(stream);

                    ReadOnlySpan<byte> relocsBytes = new(stream.PositionPointer, header.RelocsLength);
                    _relocsStream.WriteTo(stream);

                    ReadOnlySpan<byte> unwindInfosBytes = new(stream.PositionPointer, header.UnwindInfosLength);
                    _unwindInfosStream.WriteTo(stream);

                    ReadOnlySpan<byte> ptcJumpTableBytes = new(stream.PositionPointer, header.PtcJumpTableLength);
                    PtcJumpTable.Serialize(stream, PtcJumpTable);

                    header.InfosHash = XXHash128.ComputeHash(infosBytes);
                    header.CodesHash = XXHash128.ComputeHash(codesBytes);
                    header.RelocsHash = XXHash128.ComputeHash(relocsBytes);
                    header.UnwindInfosHash = XXHash128.ComputeHash(unwindInfosBytes);
                    header.PtcJumpTableHash = XXHash128.ComputeHash(ptcJumpTableBytes);

                    Debug.Assert(stream.Position == stream.Length);

                    stream.Seek(0L, SeekOrigin.Begin);
                    SerializeStructure(stream, header);

                    translatedFuncsCount = GetEntriesCount();

                    ResetCarriersIfNeeded();
                    PtcJumpTable.ClearIfNeeded();

                    using (FileStream compressedStream = new(fileName, FileMode.OpenOrCreate))
                    using (DeflateStream deflateStream = new(compressedStream, SaveCompressionLevel, true))
                    {
                        try
                        {
                            compressedStream.Write(sizeHashBytes);
                            compressedStream.Write(sizeBytes);

                            stream.Seek(0L, SeekOrigin.Begin);
                            stream.CopyTo(deflateStream);
                        }
                        catch
                        {
                            compressedStream.Position = 0L;
                        }

                        if (compressedStream.Position < compressedStream.Length)
                        {
                            compressedStream.SetLength(compressedStream.Position);
                        }
                    }
                }
            }
            finally
            {
                if (intPtr != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(intPtr);
                }
            }

            long fileSize = new FileInfo(fileName).Length;

            if (fileSize != 0L)
            {
                Logger.Info?.Print(LogClass.Ptc, $"Saved Translation Cache (size: {fileSize} bytes, translated functions: {translatedFuncsCount}).");
            }
        }

        internal static void LoadTranslations(ConcurrentDictionary<ulong, TranslatedFunction> funcs, IMemoryManager memory, JumpTable jumpTable)
        {
            if (AreCarriersEmpty())
            {
                return;
            }

            _infosStream.Seek(0L, SeekOrigin.Begin);
            _relocsStream.Seek(0L, SeekOrigin.Begin);
            _unwindInfosStream.Seek(0L, SeekOrigin.Begin);

            using (BinaryReader infosReader = new(_infosStream, EncodingCache.UTF8NoBOM, true))
            using (BinaryReader relocsReader = new(_relocsStream, EncodingCache.UTF8NoBOM, true))
            using (BinaryReader unwindInfosReader = new(_unwindInfosStream, EncodingCache.UTF8NoBOM, true))
            {
                for (int index = 0; index < GetEntriesCount(); index++)
                {
                    InfoEntry infoEntry = ReadInfo(infosReader);

                    if (infoEntry.Stubbed)
                    {
                        SkipCode(index, infoEntry.CodeLength);
                        SkipReloc(infoEntry.RelocEntriesCount);
                        SkipUnwindInfo(unwindInfosReader);
                    }
                    else if (infoEntry.HighCq || !PtcProfiler.ProfiledFuncs.TryGetValue(infoEntry.Address, out var value) || !value.HighCq)
                    {
                        byte[] code = ReadCode(index, infoEntry.CodeLength);

                        if (infoEntry.RelocEntriesCount != 0)
                        {
                            RelocEntry[] relocEntries = GetRelocEntries(relocsReader, infoEntry.RelocEntriesCount);

                            PatchCode(code.AsSpan(), relocEntries, memory.PageTablePointer, jumpTable);
                        }

                        UnwindInfo unwindInfo = ReadUnwindInfo(unwindInfosReader);

                        TranslatedFunction func = FastTranslate(code, infoEntry.GuestSize, unwindInfo, infoEntry.HighCq);

                        bool isAddressUnique = funcs.TryAdd(infoEntry.Address, func);

                        Debug.Assert(isAddressUnique, $"The address 0x{infoEntry.Address:X16} is not unique.");
                    }
                    else
                    {
                        infoEntry.Stubbed = true;
                        infoEntry.CodeLength = 0;
                        UpdateInfo(infoEntry);

                        StubCode(index);
                        StubReloc(infoEntry.RelocEntriesCount);
                        StubUnwindInfo(unwindInfosReader);
                    }
                }
            }

            if (_infosStream.Position < _infosStream.Length ||
                _relocsStream.Position < _relocsStream.Length ||
                _unwindInfosStream.Position < _unwindInfosStream.Length)
            {
                throw new Exception("Could not reach the end of one or more memory streams.");
            }

            jumpTable.Initialize(PtcJumpTable, funcs);

            PtcJumpTable.WriteJumpTable(jumpTable, funcs);
            PtcJumpTable.WriteDynamicTable(jumpTable);

            Logger.Info?.Print(LogClass.Ptc, $"{funcs.Count} translated functions loaded");
        }

        private static int GetEntriesCount()
        {
            return _codesList.Count;
        }

        private static InfoEntry ReadInfo(BinaryReader infosReader)
        {
            InfoEntry infoEntry = new InfoEntry();

            infoEntry.Address = infosReader.ReadUInt64();
            infoEntry.GuestSize = infosReader.ReadUInt64();
            infoEntry.HighCq = infosReader.ReadBoolean();
            infoEntry.Stubbed = infosReader.ReadBoolean();
            infoEntry.CodeLength = infosReader.ReadInt32();
            infoEntry.RelocEntriesCount = infosReader.ReadInt32();

            return infoEntry;
        }

        [Conditional("DEBUG")]
        private static void SkipCode(int index, int codeLength)
        {
            Debug.Assert(_codesList[index].Length == 0);
            Debug.Assert(codeLength == 0);
        }

        private static void SkipReloc(int relocEntriesCount)
        {
            _relocsStream.Seek(relocEntriesCount * RelocEntry.Stride, SeekOrigin.Current);
        }

        private static void SkipUnwindInfo(BinaryReader unwindInfosReader)
        {
            int pushEntriesLength = unwindInfosReader.ReadInt32();

            _unwindInfosStream.Seek(pushEntriesLength * UnwindPushEntry.Stride + UnwindInfo.Stride, SeekOrigin.Current);
        }

        private static byte[] ReadCode(int index, int codeLength)
        {
            Debug.Assert(_codesList[index].Length == codeLength);

            return _codesList[index];
        }

        private static RelocEntry[] GetRelocEntries(BinaryReader relocsReader, int relocEntriesCount)
        {
            RelocEntry[] relocEntries = new RelocEntry[relocEntriesCount];

            for (int i = 0; i < relocEntriesCount; i++)
            {
                int position = relocsReader.ReadInt32();
                int index = relocsReader.ReadInt32();

                relocEntries[i] = new RelocEntry(position, index);
            }

            return relocEntries;
        }

        private static void PatchCode(Span<byte> code, RelocEntry[] relocEntries, IntPtr pageTablePointer, JumpTable jumpTable)
        {
            foreach (RelocEntry relocEntry in relocEntries)
            {
                ulong imm;

                if (relocEntry.Index == PageTablePointerIndex)
                {
                    imm = (ulong)pageTablePointer.ToInt64();
                }
                else if (relocEntry.Index == JumpPointerIndex)
                {
                    imm = (ulong)jumpTable.JumpPointer.ToInt64();
                }
                else if (relocEntry.Index == DynamicPointerIndex)
                {
                    imm = (ulong)jumpTable.DynamicPointer.ToInt64();
                }
                else if (Delegates.TryGetDelegateFuncPtrByIndex(relocEntry.Index, out IntPtr funcPtr))
                {
                    imm = (ulong)funcPtr.ToInt64();
                }
                else
                {
                    throw new Exception($"Unexpected reloc entry {relocEntry}.");
                }

                BinaryPrimitives.WriteUInt64LittleEndian(code.Slice(relocEntry.Position, 8), imm);
            }
        }

        private static UnwindInfo ReadUnwindInfo(BinaryReader unwindInfosReader)
        {
            int pushEntriesLength = unwindInfosReader.ReadInt32();

            UnwindPushEntry[] pushEntries = new UnwindPushEntry[pushEntriesLength];

            for (int i = 0; i < pushEntriesLength; i++)
            {
                int pseudoOp = unwindInfosReader.ReadInt32();
                int prologOffset = unwindInfosReader.ReadInt32();
                int regIndex = unwindInfosReader.ReadInt32();
                int stackOffsetOrAllocSize = unwindInfosReader.ReadInt32();

                pushEntries[i] = new UnwindPushEntry((UnwindPseudoOp)pseudoOp, prologOffset, regIndex, stackOffsetOrAllocSize);
            }

            int prologueSize = unwindInfosReader.ReadInt32();

            return new UnwindInfo(pushEntries, prologueSize);
        }

        private static TranslatedFunction FastTranslate(byte[] code, ulong guestSize, UnwindInfo unwindInfo, bool highCq)
        {
            CompiledFunction cFunc = new CompiledFunction(code, unwindInfo);

            IntPtr codePtr = JitCache.Map(cFunc);

            GuestFunction gFunc = Marshal.GetDelegateForFunctionPointer<GuestFunction>(codePtr);

            TranslatedFunction tFunc = new TranslatedFunction(gFunc, guestSize, highCq);

            return tFunc;
        }

        private static void UpdateInfo(InfoEntry infoEntry)
        {
            _infosStream.Seek(-InfoEntry.Stride, SeekOrigin.Current);

            // WriteInfo.
            _infosWriter.Write((ulong)infoEntry.Address);
            _infosWriter.Write((ulong)infoEntry.GuestSize);
            _infosWriter.Write((bool)infoEntry.HighCq);
            _infosWriter.Write((bool)infoEntry.Stubbed);
            _infosWriter.Write((int)infoEntry.CodeLength);
            _infosWriter.Write((int)infoEntry.RelocEntriesCount);
        }

        private static void StubCode(int index)
        {
            _codesList[index] = Array.Empty<byte>();
        }

        private static void StubReloc(int relocEntriesCount)
        {
            for (int i = 0; i < relocEntriesCount * RelocEntry.Stride; i++)
            {
                _relocsStream.WriteByte(FillingByte);
            }
        }

        private static void StubUnwindInfo(BinaryReader unwindInfosReader)
        {
            int pushEntriesLength = unwindInfosReader.ReadInt32();

            for (int i = 0; i < pushEntriesLength * UnwindPushEntry.Stride + UnwindInfo.Stride; i++)
            {
                _unwindInfosStream.WriteByte(FillingByte);
            }
        }

        internal static void MakeAndSaveTranslations(ConcurrentDictionary<ulong, TranslatedFunction> funcs, IMemoryManager memory, JumpTable jumpTable)
        {
            var profiledFuncsToTranslate = PtcProfiler.GetProfiledFuncsToTranslate(funcs);

            _translateCount = 0;
            _translateTotalCount = profiledFuncsToTranslate.Count;

            int degreeOfParallelism = new DegreeOfParallelism(4d, 75d, 12.5d).GetDegreeOfParallelism(0, 32);

            if (_translateTotalCount == 0 || degreeOfParallelism == 0)
            {
                ResetCarriersIfNeeded();
                PtcJumpTable.ClearIfNeeded();

                GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;

                return;
            }

            Logger.Info?.Print(LogClass.Ptc, $"{_translateCount} of {_translateTotalCount} functions translated | Thread count: {degreeOfParallelism}");

            PtcStateChanged?.Invoke(PtcLoadingState.Start, _translateCount, _translateTotalCount);

            using AutoResetEvent progressReportEvent = new AutoResetEvent(false);

            Thread progressReportThread = new Thread(ReportProgress)
            {
                Name = "Ptc.ProgressReporter",
                Priority = ThreadPriority.Lowest,
                IsBackground = true
            };

            progressReportThread.Start(progressReportEvent);

            void TranslateFuncs()
            {
                while (profiledFuncsToTranslate.TryDequeue(out var item))
                {
                    ulong address = item.address;

                    Debug.Assert(PtcProfiler.IsAddressInStaticCodeRange(address));

                    TranslatedFunction func = Translator.Translate(memory, jumpTable, address, item.mode, item.highCq);

                    bool isAddressUnique = funcs.TryAdd(address, func);

                    Debug.Assert(isAddressUnique, $"The address 0x{address:X16} is not unique.");

                    if (func.HighCq)
                    {
                        jumpTable.RegisterFunction(address, func);
                    }

                    Interlocked.Increment(ref _translateCount);

                    if (State != PtcState.Enabled)
                    {
                        break;
                    }
                }

                Translator.DisposePools();
            }

            List<Thread> threads = new List<Thread>();

            for (int i = 0; i < degreeOfParallelism; i++)
            {
                Thread thread = new Thread(TranslateFuncs);
                thread.IsBackground = true;

                threads.Add(thread);
            }

            threads.ForEach((thread) => thread.Start());
            threads.ForEach((thread) => thread.Join());

            threads.Clear();

            progressReportEvent.Set();
            progressReportThread.Join();

            PtcStateChanged?.Invoke(PtcLoadingState.Loaded, _translateCount, _translateTotalCount);

            Logger.Info?.Print(LogClass.Ptc, $"{_translateCount} of {_translateTotalCount} functions translated | Thread count: {degreeOfParallelism}");

            PtcJumpTable.Initialize(jumpTable);

            PtcJumpTable.ReadJumpTable(jumpTable);
            PtcJumpTable.ReadDynamicTable(jumpTable);

            Thread preSaveThread = new Thread(PreSave);
            preSaveThread.IsBackground = true;
            preSaveThread.Start();
        }

        private static void ReportProgress(object state)
        {
            const int refreshRate = 50; // ms.

            AutoResetEvent endEvent = (AutoResetEvent)state;

            int count = 0;

            do
            {
                int newCount = _translateCount;

                if (count != newCount)
                {
                    PtcStateChanged?.Invoke(PtcLoadingState.Loading, newCount, _translateTotalCount);
                    count = newCount;
                }
            }
            while (!endEvent.WaitOne(refreshRate));
        }

        internal static void WriteInfoCodeRelocUnwindInfo(ulong address, ulong guestSize, bool highCq, PtcInfo ptcInfo)
        {
            lock (_lock)
            {
                // WriteInfo.
                _infosWriter.Write((ulong)address); // InfoEntry.Address
                _infosWriter.Write((ulong)guestSize); // InfoEntry.GuestSize
                _infosWriter.Write((bool)highCq); // InfoEntry.HighCq
                _infosWriter.Write((bool)false); // InfoEntry.Stubbed
                _infosWriter.Write((int)ptcInfo.Code.Length); // InfoEntry.CodeLength
                _infosWriter.Write((int)ptcInfo.RelocEntriesCount); // InfoEntry.RelocEntriesCount

                WriteCode(ptcInfo.Code.AsSpan());

                // WriteReloc.
                ptcInfo.RelocStream.WriteTo(_relocsStream);

                // WriteUnwindInfo.
                ptcInfo.UnwindInfoStream.WriteTo(_unwindInfosStream);
            }
        }

        private static void WriteCode(ReadOnlySpan<byte> code)
        {
            _codesList.Add(code.ToArray());
        }

        private static bool GetEndianness()
        {
            return BitConverter.IsLittleEndian;
        }

        private static ulong GetFeatureInfo()
        {
            return (ulong)HardwareCapabilities.FeatureInfoEdx << 32 | (uint)HardwareCapabilities.FeatureInfoEcx;
        }

        private static uint GetOSPlatform()
        {
            uint osPlatform = 0u;

            osPlatform |= (RuntimeInformation.IsOSPlatform(OSPlatform.FreeBSD) ? 1u : 0u) << 0;
            osPlatform |= (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)   ? 1u : 0u) << 1;
            osPlatform |= (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)     ? 1u : 0u) << 2;
            osPlatform |= (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? 1u : 0u) << 3;

            return osPlatform;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1/*, Size = 129*/)]
        private struct Header
        {
            public ulong Magic;

            public uint CacheFileVersion;
            public bool Endianness;
            public ulong FeatureInfo;
            public uint OSPlatform;

            public int InfosLength;
            public long CodesLength;
            public int RelocsLength;
            public int UnwindInfosLength;
            public int PtcJumpTableLength;

            public Hash128 InfosHash;
            public Hash128 CodesHash;
            public Hash128 RelocsHash;
            public Hash128 UnwindInfosHash;
            public Hash128 PtcJumpTableHash;
        }

        private struct InfoEntry
        {
            public const int Stride = 26; // Bytes.

            public ulong Address;
            public ulong GuestSize;
            public bool HighCq;
            public bool Stubbed;
            public int CodeLength;
            public int RelocEntriesCount;
        }

        private static void Enable()
        {
            State = PtcState.Enabled;
        }

        public static void Continue()
        {
            if (State == PtcState.Enabled)
            {
                State = PtcState.Continuing;
            }
        }

        public static void Close()
        {
            if (State == PtcState.Enabled ||
                State == PtcState.Continuing)
            {
                State = PtcState.Closing;
            }
        }

        internal static void Disable()
        {
            State = PtcState.Disabled;
        }

        private static void Wait()
        {
            _waitEvent.WaitOne();
        }

        public static void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;

                Wait();
                _waitEvent.Dispose();

                DisposeCarriers();
            }
        }
    }
}