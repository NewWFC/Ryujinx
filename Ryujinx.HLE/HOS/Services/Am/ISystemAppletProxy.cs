using Ryujinx.HLE.HOS.Ipc;
using System.Collections.Generic;

namespace Ryujinx.HLE.HOS.Services.Am
{
    class ISystemAppletProxy : IpcService
    {
        private Dictionary<int, ServiceProcessRequest> m_Commands;

        public override IReadOnlyDictionary<int, ServiceProcessRequest> Commands => m_Commands;

        public ISystemAppletProxy()
        {
            m_Commands = new Dictionary<int, ServiceProcessRequest>()
            {
                { 0,    GetCommonStateGetter     },
                { 1,    GetSelfController        },
                { 2,    GetWindowController      },
                { 3,    GetAudioController       },
                { 4,    GetDisplayController     },
                { 11,   GetLibraryAppletCreator  },
                { 20,   GetHomeMenuFunctions     },
                { 21,   GetGlobalStateController },
                { 22,   GetApplicationCreator    },
                { 1000, GetDebugFunctions        }
            };
        }

        public long GetCommonStateGetter(ServiceCtx Context)
        {
            MakeObject(Context, new ICommonStateGetter(Context.Device.System));

            return 0;
        }

        public long GetSelfController(ServiceCtx Context)
        {
            MakeObject(Context, new ISelfController(Context.Device.System));

            return 0;
        }

        public long GetWindowController(ServiceCtx Context)
        {
            MakeObject(Context, new IWindowController());

            return 0;
        }

        public long GetAudioController(ServiceCtx Context)
        {
            MakeObject(Context, new IAudioController());

            return 0;
        }

        public long GetDisplayController(ServiceCtx Context)
        {
            MakeObject(Context, new IDisplayController());

            return 0;
        }

        public long GetLibraryAppletCreator(ServiceCtx Context)
        {
            MakeObject(Context, new ILibraryAppletCreator());

            return 0;
        }

        public long GetHomeMenuFunctions(ServiceCtx Context)
        {
            MakeObject(Context, new IHomeMenuFunctions(Context.Device.System));

            return 0;
        }

        public long GetGlobalStateController(ServiceCtx Context)
        {
            MakeObject(Context, new IGlobalStateController());

            return 0;
        }

        public long GetApplicationCreator(ServiceCtx Context)
        {
            MakeObject(Context, new IApplicationCreator());

            return 0;
        }

        public long GetDebugFunctions(ServiceCtx Context)
        {
            MakeObject(Context, new IDebugFunctions());

            return 0;
        }
    }
}