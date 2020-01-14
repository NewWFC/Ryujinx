namespace Ryujinx.Graphics.GAL
{
    public struct BlendDescriptor
    {
        public bool Enable { get; }

        public BlendOp     ColorOp        { get; }
        public BlendFactor ColorSrcFactor { get; }
        public BlendFactor ColorDstFactor { get; }
        public BlendOp     AlphaOp        { get; }
        public BlendFactor AlphaSrcFactor { get; }
        public BlendFactor AlphaDstFactor { get; }

        public BlendDescriptor(
            bool        enable,
            BlendOp     colorOp,
            BlendFactor colorSrcFactor,
            BlendFactor colorDstFactor,
            BlendOp     alphaOp,
            BlendFactor alphaSrcFactor,
            BlendFactor alphaDstFactor)
        {
            Enable         = enable;
            ColorOp        = colorOp;
            ColorSrcFactor = colorSrcFactor;
            ColorDstFactor = colorDstFactor;
            AlphaOp        = alphaOp;
            AlphaSrcFactor = alphaSrcFactor;
            AlphaDstFactor = alphaDstFactor;
        }
    }
}
