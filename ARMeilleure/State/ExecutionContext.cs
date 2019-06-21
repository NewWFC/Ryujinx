using System;
using System.Diagnostics;

namespace ARMeilleure.State
{
    public class ExecutionContext : IDisposable
    {
        private NativeContext _nativeContext;

        internal IntPtr NativeContextPtr => _nativeContext.BasePtr;

        private static Stopwatch _tickCounter;

        private static double _hostTickFreq;

        public uint CtrEl0   => 0x8444c004;
        public uint DczidEl0 => 0x00000004;

        public ulong CntfrqEl0 { get; set; }
        public ulong CntpctEl0
        {
            get
            {
                double ticks = _tickCounter.ElapsedTicks * _hostTickFreq;

                return (ulong)(ticks * CntfrqEl0);
            }
        }

        public long TpidrEl0 { get; set; }
        public long Tpidr    { get; set; }

        public FPCR Fpcr { get; set; }
        public FPSR Fpsr { get; set; }

        public event EventHandler<InstExceptionEventArgs> Break;
        public event EventHandler<InstExceptionEventArgs> SupervisorCall;
        public event EventHandler<InstUndefinedEventArgs> Undefined;

        static ExecutionContext()
        {
            _hostTickFreq = 1.0 / Stopwatch.Frequency;

            _tickCounter = new Stopwatch();

            _tickCounter.Start();
        }

        public ExecutionContext()
        {
            _nativeContext = new NativeContext();
        }

        public ulong GetX(int index)              => _nativeContext.GetX(index);
        public void  SetX(int index, ulong value) => _nativeContext.SetX(index, value);

        public V128 GetV(int index)             => _nativeContext.GetV(index);
        public void SetV(int index, V128 value) => _nativeContext.SetV(index, value);

        public bool GetPstateFlag(PState flag)             => _nativeContext.GetPstateFlag(flag);
        public void SetPstateFlag(PState flag, bool value) => _nativeContext.SetPstateFlag(flag, value);

        internal void OnBreak(ulong address, int imm)
        {
            Break?.Invoke(this, new InstExceptionEventArgs(address, imm));
        }

        internal void OnSupervisorCall(ulong address, int imm)
        {
            SupervisorCall?.Invoke(this, new InstExceptionEventArgs(address, imm));
        }

        internal void OnUndefined(ulong address, int opCode)
        {
            Undefined?.Invoke(this, new InstUndefinedEventArgs(address, opCode));
        }

        public void Dispose()
        {
            _nativeContext.Dispose();
        }
    }
}