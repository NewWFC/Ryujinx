﻿using Ryujinx.Horizon.Sdk.OsTypes;
using Ryujinx.Horizon.Kernel;

namespace Ryujinx.HLE.HOS.Services.Am
{
    class AmServer : ServerBase
    {
        private readonly Switch _device;

        private SystemEventType _messageEvent;
        private SignalableEvent _messageEventSignalable;
        private SystemEventType _displayResolutionChangedEvent;
        private SignalableEvent _displayResolutionChangedEventSignalable;

        public AmServer(Switch device) : base(device, "AmServer")
        {
            _device = device;
        }

        protected override void Initialize()
        {
            Os.CreateSystemEvent(out _messageEvent, EventClearMode.AutoClear, true);
            Os.CreateSystemEvent(out _displayResolutionChangedEvent, EventClearMode.AutoClear, true);

            _messageEventSignalable                  = KernelStatic.GetSignalableEvent(Os.GetWritableHandleOfSystemEvent(ref _messageEvent));
            _displayResolutionChangedEventSignalable = KernelStatic.GetSignalableEvent(Os.GetWritableHandleOfSystemEvent(ref _displayResolutionChangedEvent));

            _device.System.AppletState.SetFocus(true);
        }

        public void SignalMessage() => _messageEventSignalable.Signal();
        public void ClearMessage() => _messageEventSignalable.Clear();
        public void SignalDisplayResolutionChanged() => _displayResolutionChangedEventSignalable.Signal();
        public int GetMessageEventHandle() => Os.GetReadableHandleOfSystemEvent(ref _messageEvent);
        public int GetDisplayResolutionChangedEventHandle() => Os.GetReadableHandleOfSystemEvent(ref _displayResolutionChangedEvent);
    }
}
