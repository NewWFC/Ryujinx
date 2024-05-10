using Ryujinx.Common;
using Ryujinx.Graphics.GAL;
using System;

namespace Ryujinx.Graphics.Gpu.Memory
{
    /// <summary>
    /// Manages flushing ranges from buffers in advance for easy access, if they are flushed often.
    /// Typically, from device local memory to a host mapped target for cached access.
    /// </summary>
    internal class BufferPreFlush : IDisposable
    {
        private const ulong PageSize = MemoryManager.PageSize;

        /// <summary>
        /// Threshold for the number of copies without a flush required to disable preflush on a page.
        /// </summary>
        private const int DeactivateCopyThreshold = 200;

        private enum PreFlushState
        {
            None,
            HasFlushed,
            HasCopied
        }

        private struct PreFlushPage
        {
            public PreFlushState State;
            public ulong FirstActivatedSync;
            public ulong LastCopiedSync;
            public int CopyCount;
        }

        /// <summary>
        /// True if there are ranges that should copy to the flush buffer, false otherwise.
        /// </summary>
        public bool ShouldCopy { get; private set; }

        private readonly GpuContext _context;
        private readonly Buffer _buffer;
        private readonly PreFlushPage[] _pages;
        private readonly ulong _address;
        private readonly ulong _size;
        private readonly ulong _misalignment;
        private readonly Action<BufferHandle, ulong, ulong> _flushAction;

        private BufferHandle _flushBuffer;

        public BufferPreFlush(GpuContext context, Buffer parent, Action<BufferHandle, ulong, ulong> flushAction)
        {
            _context = context;
            _buffer = parent;
            _address = parent.Address;
            _size = parent.Size;
            _pages = new PreFlushPage[BitUtils.DivRoundUp(_size, PageSize)];
            _misalignment = _address & (PageSize - 1);

            _flushAction = flushAction;
        }

        public void EnsureFlushBuffer()
        {
            if (_flushBuffer == BufferHandle.Null)
            {
                _flushBuffer = _context.Renderer.CreateBuffer((int)_size, BufferAccess.HostMemory);
            }
        }

        private (int index, int count) GetPageRange(ulong address, ulong size)
        {
            ulong offset = address - _address;
            ulong endOffset = offset + size;

            int basePage = (int)(offset / PageSize);
            int endPage = (int)((endOffset - 1) / PageSize);

            return (basePage, 1 + endPage - basePage);
        }

        private (int offset, int size) GetOffset(int startPage, int count)
        {
            int offset = (int)((ulong)startPage * PageSize - _misalignment);
            int endOffset = (int)((ulong)(startPage + count) * PageSize - _misalignment);

            offset = Math.Max(0, offset);
            endOffset = Math.Min((int)_size, endOffset);

            return (offset, endOffset - offset);
        }

        private void CopyPageRange(int startPage, int count)
        {
            (int offset, int size) = GetOffset(startPage, count);

            EnsureFlushBuffer();

            _context.Renderer.Pipeline.CopyBuffer(_buffer.Handle, _flushBuffer, offset, offset, size);
        }

        public void CopyModified(ulong address, ulong size)
        {
            (int baseIndex, int count) = GetPageRange(address, size);
            ulong syncNumber = _context.SyncNumber;

            int startPage = -1;

            for (int i = 0; i < count; i++)
            {
                int pageIndex = baseIndex + i;
                ref PreFlushPage page = ref _pages[pageIndex];

                if (page.State > PreFlushState.None)
                {
                    // Perform the copy, and update the state of each page.
                    if (startPage == -1)
                    {
                        startPage = pageIndex;
                    }

                    if (page.State != PreFlushState.HasCopied)
                    {
                        page.FirstActivatedSync = syncNumber;
                        page.State = PreFlushState.HasCopied;
                    }
                    else if (page.CopyCount++ >= DeactivateCopyThreshold)
                    {
                        page.CopyCount = 0;
                        page.State = PreFlushState.None;
                    }

                    if (page.LastCopiedSync != syncNumber)
                    {
                        page.LastCopiedSync = syncNumber;
                    }
                }
                else if (startPage != -1)
                {
                    CopyPageRange(startPage, pageIndex - startPage);

                    startPage = -1;
                }
            }

            if (startPage != -1)
            {
                CopyPageRange(startPage, (baseIndex + count) - startPage);
            }
        }

        private void FlushPageRange(ulong address, ulong size, int startPage, int count, bool preFlush)
        {
            (int pageOffset, int pageSize) = GetOffset(startPage, count);

            int offset = (int)(address - _address);
            int end = offset + (int)size;

            offset = Math.Max(offset, pageOffset);
            end = Math.Min(end, pageOffset + pageSize);

            if (end >= offset)
            {
                BufferHandle handle = preFlush ? _flushBuffer : _buffer.Handle;
                _flushAction(handle, _address + (ulong)offset, (ulong)(end - offset));
            }
        }

        public void FlushWithAction(ulong address, ulong size, ulong syncNumber)
        {
            // Copy the parts of the range that have pre-flush copies that have been completed.
            // Run the flush action for ranges that don't have pre-flush copies.

            // If a range doesn't have a pre-flush copy, consider adding one.

            (int baseIndex, int count) = GetPageRange(address, size);

            bool rangePreFlushed = false;
            int startPage = -1;

            for (int i = 0; i < count; i++)
            {
                int pageIndex = baseIndex + i;
                ref PreFlushPage page = ref _pages[pageIndex];

                bool flushPage = false;
                page.CopyCount = 0;

                if (page.State == PreFlushState.HasCopied)
                {
                    if (syncNumber >= page.FirstActivatedSync)
                    {
                        // After the range is first activated, its data will always be copied to the preflush buffer on each sync.
                        flushPage = true;
                    }
                }
                else if (page.State == PreFlushState.None)
                {
                    page.State = PreFlushState.HasFlushed;
                    ShouldCopy = true;
                }

                if (flushPage)
                {
                    if (!rangePreFlushed || startPage == -1)
                    {
                        if (startPage != -1)
                        {
                            FlushPageRange(address, size, startPage, pageIndex - startPage, false);
                        }

                        rangePreFlushed = true;
                        startPage = pageIndex;
                    }
                }
                else if (rangePreFlushed || startPage == -1)
                {
                    if (startPage != -1)
                    {
                        FlushPageRange(address, size, startPage, pageIndex - startPage, true);
                    }

                    rangePreFlushed = false;
                    startPage = pageIndex;
                }
            }

            if (startPage != -1)
            {
                FlushPageRange(address, size, startPage, (baseIndex + count) - startPage, rangePreFlushed);
            }
        }

        public void Dispose()
        {
            if (_flushBuffer != BufferHandle.Null)
            {
                _context.Renderer.DeleteBuffer(_flushBuffer);
            }
        }
    }
}
