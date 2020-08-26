﻿using Ryujinx.Common;
using Ryujinx.Common.Logging;
using Ryujinx.HLE.HOS.Ipc;
using Ryujinx.HLE.HOS.Services.Bcat.ServiceCreator.Types;
using Ryujinx.Horizon.Sdk.OsTypes;
using System;
using System.IO;

namespace Ryujinx.HLE.HOS.Services.Bcat.ServiceCreator
{
    class IDeliveryCacheProgressService : IpcService, IDisposable
    {
        private SystemEventType _event;

        public IDeliveryCacheProgressService(ServiceCtx context)
        {
            Os.CreateSystemEvent(out _event, EventClearMode.AutoClear, true);
        }

        [Command(0)]
        // GetEvent() -> handle<copy>
        public ResultCode GetEvent(ServiceCtx context)
        {
            context.Response.HandleDesc = IpcHandleDesc.MakeCopy(Os.GetReadableHandleOfSystemEvent(ref _event));

            Logger.Stub?.PrintStub(LogClass.ServiceBcat);

            return ResultCode.Success;
        }

        [Command(1)]
        // GetImpl() -> buffer<nn::bcat::detail::DeliveryCacheProgressImpl, 0x1a>
        public ResultCode GetImpl(ServiceCtx context)
        {
            DeliveryCacheProgressImpl deliveryCacheProgress = new DeliveryCacheProgressImpl
            {
                State  = DeliveryCacheProgressImpl.Status.Done,
                Result = 0
            };

            WriteDeliveryCacheProgressImpl(context, context.Request.RecvListBuff[0], deliveryCacheProgress);

            Logger.Stub?.PrintStub(LogClass.ServiceBcat);

            return ResultCode.Success;
        }

        private void WriteDeliveryCacheProgressImpl(ServiceCtx context, IpcRecvListBuffDesc ipcDesc, DeliveryCacheProgressImpl deliveryCacheProgress)
        {
            using (MemoryStream memory = new MemoryStream((int)ipcDesc.Size))
            using (BinaryWriter bufferWriter = new BinaryWriter(memory))
            {
                bufferWriter.WriteStruct(deliveryCacheProgress);
                context.Memory.Write((ulong)ipcDesc.Position, memory.ToArray());
            }
        }

        public void Dispose()
        {
            Os.DestroySystemEvent(ref _event);
        }
    }
}