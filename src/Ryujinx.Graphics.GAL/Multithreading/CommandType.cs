namespace Ryujinx.Graphics.GAL.Multithreading
{
    enum CommandType : byte
    {
        Action,
        CreateBufferAccess,
        CreateBufferSparse,
        CreateHostBuffer,
        CreateImageArray,
        CreateProgram,
        CreateSampler,
        CreateSync,
        CreateTexture,
        CreateTextureArray,
        GetCapabilities,
        Unused,
        PreFrame,
        ReportCounter,
        ResetCounter,
        UpdateCounters,

        BufferDispose,
        BufferGetData,
        BufferSetData,

        CounterEventDispose,
        CounterEventFlush,

        ImageArrayDispose,
        ImageArraySetFormats,
        ImageArraySetImages,

        ProgramDispose,
        ProgramGetBinary,
        ProgramCheckLink,

        SamplerDispose,

        TextureCopyTo,
        TextureCopyToBuffer,
        TextureCopyToScaled,
        TextureCopyToSlice,
        TextureCreateView,
        TextureGetData,
        TextureGetDataSlice,
        TextureRelease,
        TextureSetData,
        TextureSetDataSlice,
        TextureSetDataSliceRegion,
        TextureSetStorage,

        TextureArrayDispose,
        TextureArraySetSamplers,
        TextureArraySetTextures,

        WindowPresent,

        Barrier,
        BeginTransformFeedback,
        ClearBuffer,
        ClearRenderTargetColor,
        ClearRenderTargetDepthStencil,
        CommandBufferBarrier,
        CopyBuffer,
        DispatchCompute,
        Draw,
        DrawIndexed,
        DrawIndexedIndirect,
        DrawIndexedIndirectCount,
        DrawIndirect,
        DrawIndirectCount,
        DrawTexture,
        EndHostConditionalRendering,
        EndTransformFeedback,
        SetAlphaTest,
        SetBlendStateAdvanced,
        SetBlendState,
        SetDepthBias,
        SetDepthClamp,
        SetDepthMode,
        SetDepthTest,
        SetFaceCulling,
        SetFrontFace,
        SetStorageBuffers,
        SetTransformFeedbackBuffers,
        SetUniformBuffers,
        SetImage,
        SetImageArray,
        SetImageArraySeparate,
        SetIndexBuffer,
        SetLineParameters,
        SetLogicOpState,
        SetMultisampleState,
        SetPatchParameters,
        SetPointParameters,
        SetPolygonMode,
        SetPrimitiveRestart,
        SetPrimitiveTopology,
        SetProgram,
        SetRasterizerDiscard,
        SetRenderTargetColorMasks,
        SetRenderTargets,
        SetScissor,
        SetStencilTest,
        SetTextureAndSampler,
        SetTextureArray,
        SetTextureArraySeparate,
        SetUserClipDistance,
        SetVertexAttribs,
        SetVertexBuffers,
        SetViewports,
        TextureBarrier,
        TextureBarrierTiled,
        TryHostConditionalRendering,
        TryHostConditionalRenderingFlush,
    }
}
