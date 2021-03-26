﻿using Ryujinx.Graphics.GAL.Multithreading.Model;
using Ryujinx.Graphics.GAL.Multithreading.Resources;

namespace Ryujinx.Graphics.GAL.Multithreading.Commands.Renderer
{
    struct CreateSamplerCommand : IGALCommand
    {
        public CommandType CommandType => CommandType.CreateSampler;
        private TableRef<ThreadedSampler> _sampler;
        private SamplerCreateInfo _info;

        public void Set(TableRef<ThreadedSampler> sampler, SamplerCreateInfo info)
        {
            _sampler = sampler;
            _info = info;
        }

        public void Run(ThreadedRenderer threaded, IRenderer renderer)
        {
            _sampler.Get(threaded).Base = renderer.CreateSampler(_info);
        }
    }
}
