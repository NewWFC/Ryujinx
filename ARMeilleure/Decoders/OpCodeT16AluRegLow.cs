﻿namespace ARMeilleure.Decoders
{
    class OpCodeT16AluRegLow : OpCodeT16, IOpCode32AluReg
    {
        public int Rm { get; }
        public int Rd { get; }
        public int Rn { get; }

        public bool SetFlags { get; }

        public static new OpCode Create(InstDescriptor inst, ulong address, int opCode, bool inITBlock) => new OpCodeT16AluRegLow(inst, address, opCode, inITBlock);

        public OpCodeT16AluRegLow(InstDescriptor inst, ulong address, int opCode, bool inITBlock) : base(inst, address, opCode, inITBlock)
        {
            Rd = (opCode >> 0) & 0x7;
            Rn = (opCode >> 0) & 0x7;
            Rm = (opCode >> 3) & 0x7;

            SetFlags = !inITBlock;
        }
    }
}