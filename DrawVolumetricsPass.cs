using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RendererUtils;
using UnityEngine.Rendering.Universal;
using Unity.Collections;

namespace Playtonic.Game
{
    internal class DrawVolumetricsPass : ScriptableRenderPass
    {

        public FilterMode filterMode { get; set; }
        public DrawVolumetricsFeature.Settings settings;

    // --------------------------------------------------------------------

        private RenderTargetIdentifier source;
        private RenderTargetIdentifier destination;
        private int temporaryRTId = Shader.PropertyToID("_TempRT");

        private int mCloudDepthID = Shader.PropertyToID("_mainDepthTexture");
        private int mCloudID = Shader.PropertyToID("_mainCloudTexture");
        private int mCloudNoiseID = Shader.PropertyToID("_cloudNoiseTexture");

        private int mSourceID;
        private int mDestinationID;
        private bool mIsSourceAndDestinationSameTarget;

        private string m_ProfilerTag;
        private FilteringSettings mFilteringSettings;
        private RendererListDesc mRendererListDesc;
        private RenderTextureDescriptor mDefaultRenderTarget;

        private RenderTargetIdentifier mCameraColorTarget;

        private List<ShaderTagId> mShaderTagIdList = new List<ShaderTagId>();
        
    // --------------------------------------------------------------------

        public DrawVolumetricsPass(string tag, string[] shaderTags, int layerMask)
        {
            if (shaderTags != null && shaderTags.Length > 0)
            {
                foreach (var passName in shaderTags)
                    mShaderTagIdList.Add(new ShaderTagId(passName));
            }
            else
            {
                //mShaderTagIdList.Add(new ShaderTagId("SRPDefaultUnlit"));
                mShaderTagIdList.Add(new ShaderTagId("UniversalForward"));
                mShaderTagIdList.Add(new ShaderTagId("UniversalForwardOnly"));
            }
            mFilteringSettings = new FilteringSettings(RenderQueueRange.transparent, layerMask);
        }

    // --------------------------------------------------------------------

        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
            mDefaultRenderTarget = cameraTextureDescriptor;
            Shader.SetGlobalTexture(mCloudNoiseID, settings.cloudNoiseTexture);
        }

    // --------------------------------------------------------------------

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            RenderTextureDescriptor blitTargetDescriptor = renderingData.cameraData.cameraTargetDescriptor;
            blitTargetDescriptor.depthBufferBits = 0;

            mIsSourceAndDestinationSameTarget = settings.sourceType == settings.destinationType &&
                (settings.sourceType == BufferType.CameraColor || settings.sourceTextureId == settings.destinationTextureId);

            var renderer = renderingData.cameraData.renderer;

            if (settings.sourceType == BufferType.CameraColor)
            {
                mSourceID = -1;
                source = renderer.cameraColorTarget;
            }
            else
            {
                mSourceID = Shader.PropertyToID(settings.sourceTextureId);
                cmd.GetTemporaryRT(mSourceID, blitTargetDescriptor, filterMode);
                source = new RenderTargetIdentifier(mSourceID);
            }

            if (mIsSourceAndDestinationSameTarget)
            {
                mDestinationID = temporaryRTId;
                cmd.GetTemporaryRT(mDestinationID, blitTargetDescriptor, filterMode);
                destination = new RenderTargetIdentifier(mDestinationID);
            }
            else if (settings.destinationType == BufferType.CameraColor)
            {
                mDestinationID = -1;
                destination = renderer.cameraColorTarget;
            }
            else
            {
                mDestinationID = Shader.PropertyToID(settings.destinationTextureId);
                cmd.GetTemporaryRT(mDestinationID, blitTargetDescriptor, filterMode);
                destination = new RenderTargetIdentifier(mDestinationID);
            }

        }

    // --------------------------------------------------------------------

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {       
            if(settings.safePassFunction)
               ExecuteOldPass(context, ref renderingData);
            else
                ExecutePass(context, ref renderingData);
        }

    // --------------------------------------------------------------------

        public void ExecuteOldPass(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            var cameraColorTarget = renderingData.cameraData.renderer.cameraColorTarget;
            var cameraDepthTarget = renderingData.cameraData.renderer.cameraDepthTarget;
            var renderDescriptor = renderingData.cameraData.cameraTargetDescriptor;

            DrawingSettings drawingSettings = CreateDrawingSettings(mShaderTagIdList, ref renderingData, SortingCriteria.CommonTransparent);
            drawingSettings.overrideMaterial = null;
            drawingSettings.overrideMaterialPassIndex = 0;

            mRendererListDesc = new RendererListDesc(mShaderTagIdList.ToArray(), renderingData.cullResults, renderingData.cameraData.camera);
            var rendererList = context.CreateRendererList(mRendererListDesc);

            CommandBuffer cmd = CommandBufferPool.Get();

            using (new ProfilingScope(cmd, new ProfilingSampler(m_ProfilerTag)))
            {
                cmd.GetTemporaryRT( mCloudDepthID, (int)(renderDescriptor.width*settings.renderScale), (int)(renderDescriptor.height*settings.renderScale), 0,
                                    FilterMode.Bilinear, RenderTextureFormat.R8, RenderTextureReadWrite.Default, 1, false);
                cmd.GetTemporaryRT( mCloudID, (int)(renderDescriptor.width*settings.renderScale), (int)(renderDescriptor.height*settings.renderScale), 0,
                                    FilterMode.Trilinear, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default, 1, false);

                //cmd.Blit(cameraColorTarget, mCloudID, settings.blitMaterial);
                cmd.SetRenderTarget(mCloudID);
                cmd.ClearRenderTarget(true, true, new Color(0.0f,0.0f,0.0f,0.0f), 1.0f);
                //cmd.Blit(cameraColorTarget, mCloudID, settings.blitMaterial);
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                context.DrawRenderers(renderingData.cullResults, ref drawingSettings, ref mFilteringSettings);

                cmd.SetGlobalVector("textureSize", new Vector4(renderDescriptor.width,renderDescriptor.height,0,0));
                cmd.Blit(mCloudID, cameraColorTarget, settings.blitMaterial);
                cmd.ReleaseTemporaryRT(mCloudID);
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();
                CommandBufferPool.Release(cmd);  
            }
        }

    // --------------------------------------------------------------------

    // Unfinished render pass using Unity's new way to switch render targets
    // https://docs.unity3d.com/2020.1/Documentation/ScriptReference/Rendering.ScriptableRenderContext.BeginRenderPass.html
        public void ExecutePass(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, new ProfilingSampler(m_ProfilerTag)))
            {
                DrawingSettings drawingSettings = CreateDrawingSettings(mShaderTagIdList, ref renderingData, SortingCriteria.CommonTransparent);
                drawingSettings.overrideMaterial = null;
                drawingSettings.overrideMaterialPassIndex = 0;

                mRendererListDesc = new RendererListDesc(mShaderTagIdList.ToArray(), renderingData.cullResults, renderingData.cameraData.camera);
                var rendererList = context.CreateRendererList(mRendererListDesc);

                var cameraColorTarget = renderingData.cameraData.renderer.cameraColorTarget;
                var cameraDepthTarget = renderingData.cameraData.renderer.cameraDepthTarget;
                var renderDescriptor = renderingData.cameraData.cameraTargetDescriptor;

                var albedo = new AttachmentDescriptor(RenderTextureFormat.ARGB32);
                var depth = new AttachmentDescriptor(RenderTextureFormat.Depth);

                depth.ConfigureClear(new Color(), 1.0f, 0);
                albedo.ConfigureClear(new Color(), 1.0f, 0);

                var attachments = new NativeArray<AttachmentDescriptor>(2, Allocator.Temp);
                const int depthIndex = 0, albedoIndex = 1;
                attachments[depthIndex] = depth;
                attachments[albedoIndex] = albedo;
                context.BeginRenderPass((int)(renderDescriptor.width*settings.renderScale), (int)(renderDescriptor.height*settings.renderScale), 1, attachments, depthIndex);
                    attachments.Dispose();
                    
                    var gbufferColors = new NativeArray<int>(1, Allocator.Temp);
                    gbufferColors[0] = albedoIndex;

                    context.BeginSubPass(gbufferColors);

                        gbufferColors.Dispose();

                        cmd.ClearRenderTarget(true, true, new Color(0.0f,0.0f,0.0f,0.0f), 0.5f);
                        
                        context.ExecuteCommandBuffer(cmd);
                        cmd.Clear();

                        context.DrawRenderers(renderingData.cullResults, ref drawingSettings, ref mFilteringSettings);

                        this.Blit(cmd, BuiltinRenderTextureType.CurrentActive, cameraColorTarget, settings.blitMaterial);

                        context.ExecuteCommandBuffer(cmd);
                        cmd.Clear();
                        CommandBufferPool.Release(cmd);

                    context.EndSubPass();

                context.EndRenderPass();
            }
        }

    // --------------------------------------------------------------------

        public override void FrameCleanup(CommandBuffer cmd)
        {
            if (mDestinationID != -1)
                cmd.ReleaseTemporaryRT(mDestinationID);

            if (source == destination && mSourceID != -1)
                cmd.ReleaseTemporaryRT(mSourceID);
        }
    }
}